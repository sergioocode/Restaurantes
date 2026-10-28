using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.CustomerQr.Pwa.Api;
using Restaurantes.Clients.CustomerQr.Pwa.Models;

namespace Restaurantes.Clients.CustomerQr.Pwa.Pages;

public partial class QrOrder
{
    [Parameter]
    public string QrCode { get; set; } = string.Empty;

    private QrSessionEnvelope? context;
    private RestaurantResponse? restaurant;
    private List<MenuItemResponse> menu = [];
    private readonly Dictionary<Guid, int> quantities = [];
    private readonly Dictionary<Guid, string> notes = [];
    private bool loading = true;
    private bool busy;
    private bool sessionExpired;
    private bool qrOrderSubmitted;
    private bool renewingQrReservation;
    private string error = string.Empty;
    private string success = string.Empty;
    private string progress = string.Empty;
    private OrderResponse? pendingOrder;
    private Guid pendingPaymentKey;
    private List<OrderResponse> customerOrders = [];
    private CancellationTokenSource? statusPolling;
    private CancellationTokenSource? sessionPolling;
    private string ordersStorageKey = string.Empty;
    private DateTime lastQrActivityAtUtc;
    private DateTime lastQrTouchAtUtc;

    protected override void OnInitialized()
    {
        CatalogRealtime.Changed += HandleCatalogChanged;
    }

    private IEnumerable<IGrouping<string, MenuItemResponse>> Categories =>
        menu.Where(x => x.IsAvailable)
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .GroupBy(x => x.CategoryName);

    private int ItemCount => quantities.Values.Sum();
    private decimal Total => menu.Sum(x => x.Price * Quantity(x.ProductId));
    private string ReservationCountdown
    {
        get
        {
            if (
                qrOrderSubmitted
                || context?.Session?.QrReservationExpiresAtUtc is not DateTime expiresAtUtc
            )
            {
                return string.Empty;
            }

            int seconds = Math.Max(
                0,
                (int)Math.Ceiling((expiresAtUtc - DateTime.UtcNow).TotalSeconds)
            );
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }

    protected override Task OnParametersSetAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        await StopPollingAsync();

        loading = true;
        sessionExpired = false;
        error = string.Empty;
        success = string.Empty;
        string storageKey = $"restaurantes.qr.{QrCode.Trim().ToUpperInvariant()}";
        try
        {
            string? savedToken = await Js.InvokeAsync<string?>("localStorage.getItem", storageKey);
            Guid? previousSessionId = context?.Session?.Id;
            Guid? previousTableId = context?.Table.Id;
            context = await Api.PreviewAsync(QrCode, savedToken);
            if (!context.IsRestaurantOpen)
            {
                await CatalogRealtime.DisconnectAsync();
                menu = [];
                customerOrders = [];
                ordersStorageKey = string.Empty;
                return;
            }
            if (previousTableId.HasValue && previousTableId != context.Table.Id)
            {
                quantities.Clear();
                notes.Clear();
            }
            context = await Api.OpenSessionAsync(QrCode, context.CustomerAccessToken);
            if (context.Session is null)
            {
                throw new InvalidOperationException("No se pudo reservar la mesa para el pedido.");
            }
            await Js.InvokeVoidAsync(
                "localStorage.setItem",
                storageKey,
                context.CustomerAccessToken
            );
            if (previousSessionId != context.Session.Id)
            {
                pendingOrder = null;
                pendingPaymentKey = Guid.Empty;
            }

            qrOrderSubmitted = context.Session.QrReservationExpiresAtUtc is null;
            lastQrActivityAtUtc = DateTime.UtcNow;
            lastQrTouchAtUtc = lastQrActivityAtUtc;
            restaurant = await Api.RestaurantAsync(context.Table.RestaurantId);
            menu = await Api.MenuAsync(context.Table.RestaurantId);
            await CatalogRealtime.ConnectAsync(context.Table.RestaurantId);
            customerOrders = [];
            ordersStorageKey = string.Empty;
            ordersStorageKey = $"restaurantes.qr.orders.{context.Session.Id:N}";
            string? savedOrders = await Js.InvokeAsync<string?>(
                "localStorage.getItem",
                ordersStorageKey
            );
            customerOrders = string.IsNullOrWhiteSpace(savedOrders)
                ? []
                : JsonSerializer
                    .Deserialize<List<Guid>>(savedOrders)
                    ?.Select(id => new OrderResponse(
                        id,
                        context.Table.RestaurantId,
                        "Submitted",
                        0,
                        0,
                        DateTime.UtcNow
                    ))
                    .ToList()
                    ?? [];
            if (customerOrders.Count > 0)
            {
                await RefreshOrderStatusesAsync();
                StartStatusPolling();
            }
            StartSessionPolling();
        }
        catch (CustomerApiException exception)
        {
            if (exception.StatusCode == HttpStatusCode.Gone)
            {
                await ExpireCustomerSessionAsync(storageKey);
            }
            else
            {
                error =
                    exception.StatusCode == HttpStatusCode.Forbidden
                        ? "Conéctate a la red autorizada del restaurante y vuelve a escanear el QR."
                        : exception.Message;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            loading = false;
        }
    }

    private int Quantity(Guid productId)
    {
        return quantities.GetValueOrDefault(productId);
    }

    private string Note(Guid productId)
    {
        return notes.GetValueOrDefault(productId) ?? string.Empty;
    }

    private void Change(Guid productId, int delta)
    {
        quantities[productId] = Math.Clamp(Quantity(productId) + delta, 0, 100);
    }

    private async Task RegisterQrActivity()
    {
        if (!qrOrderSubmitted && context?.Session?.QrReservationExpiresAtUtc is not null)
        {
            lastQrActivityAtUtc = DateTime.UtcNow;
            if (
                context.Session.QrReservationExpiresAtUtc <= lastQrActivityAtUtc.AddSeconds(30)
                && lastQrActivityAtUtc - lastQrTouchAtUtc >= TimeSpan.FromSeconds(15)
            )
            {
                await RenewQrReservationAsync(context.Session);
            }
        }
    }

    private async Task RenewQrReservationAsync(QrDiningSessionResponse session)
    {
        if (context is null || qrOrderSubmitted || renewingQrReservation)
        {
            return;
        }

        renewingQrReservation = true;
        try
        {
            QrReservationActivityResponse activity = await Api.TouchReservationAsync(
                session.Id,
                context.CustomerAccessToken
            );
            context = context with
            {
                Session = session with
                {
                    QrReservationExpiresAtUtc = activity.QrReservationExpiresAtUtc,
                },
            };
            qrOrderSubmitted = activity.QrReservationExpiresAtUtc is null;
            lastQrTouchAtUtc = DateTime.UtcNow;
        }
        catch (CustomerApiException exception) when (exception.StatusCode == HttpStatusCode.Gone)
        {
            await ExpireCustomerSessionAsync($"restaurantes.qr.{QrCode.Trim().ToUpperInvariant()}");
        }
        finally
        {
            renewingQrReservation = false;
        }
    }

    private void SetNote(Guid productId, string? value)
    {
        notes[productId] = value ?? string.Empty;
    }

    private async Task SubmitOrderAsync()
    {
        if (context is null || ItemCount == 0)
        {
            return;
        }

        busy = true;
        error = string.Empty;
        success = string.Empty;
        try
        {
            progress = "Validando presencia…";
            if (context.Session is null)
            {
                throw new InvalidOperationException("La sesión QR no está disponible.");
            }
            QrReservationActivityResponse activity = await Api.TouchReservationAsync(
                context.Session.Id,
                context.CustomerAccessToken
            );
            context = context with
            {
                Session = context.Session with
                {
                    QrReservationExpiresAtUtc = activity.QrReservationExpiresAtUtc,
                },
            };
            qrOrderSubmitted = activity.QrReservationExpiresAtUtc is null;
            ordersStorageKey = $"restaurantes.qr.orders.{context.Session.Id:N}";
            OrderResponse order;
            if (pendingOrder is null)
            {
                progress = "Creando pedido…";
                pendingOrder = await Api.CreateOrderAsync(context, quantities, notes);
                pendingPaymentKey = Guid.NewGuid();
            }
            order = pendingOrder;

            progress = "Confirmando pago…";
            await RetryAsync(
                () =>
                    Api.CaptureOnlineAsync(
                        order.Id,
                        context.Session.Id,
                        pendingPaymentKey,
                        context.CustomerAccessToken
                    ),
                status => status == (HttpStatusCode)425
            );

            progress = "Enviando a cocina…";
            OrderResponse submitted = await RetryAsync(
                () => Api.SubmitAsync(order.Id, context.CustomerAccessToken),
                status => status == HttpStatusCode.Conflict
            );

            quantities.Clear();
            notes.Clear();
            pendingOrder = null;
            pendingPaymentKey = Guid.Empty;
            customerOrders.RemoveAll(x => x.Id == submitted.Id);
            customerOrders.Add(submitted);
            qrOrderSubmitted = true;
            await PersistOrderIdsAsync();
            StartStatusPolling();
            success =
                $"Pedido {submitted.Id.ToString()[..8]} · {submitted.Total:0.00} € pagado y enviado.";
        }
        catch (Exception exception)
        {
            if (exception is CustomerApiException { StatusCode: HttpStatusCode.Gone })
            {
                await ExpireCustomerSessionAsync(
                    $"restaurantes.qr.{QrCode.Trim().ToUpperInvariant()}"
                );
            }
            else
            {
                error = exception.Message;
            }
        }
        finally
        {
            busy = false;
            progress = string.Empty;
        }
    }

    private void StartStatusPolling()
    {
        statusPolling?.Cancel();
        statusPolling?.Dispose();
        statusPolling = new CancellationTokenSource();
        _ = PollOrderStatusesAsync(statusPolling.Token);
    }

    private void StartSessionPolling()
    {
        sessionPolling?.Cancel();
        sessionPolling?.Dispose();
        sessionPolling = new CancellationTokenSource();
        _ = PollQrSessionAsync(sessionPolling.Token);
    }

    private async Task PollQrSessionAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        DateTime lastSessionCheckAtUtc = DateTime.UtcNow;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    StateHasChanged();
                    if (DateTime.UtcNow - lastSessionCheckAtUtc < TimeSpan.FromSeconds(15))
                    {
                        return;
                    }

                    lastSessionCheckAtUtc = DateTime.UtcNow;
                    await RefreshQrSessionAsync();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshQrSessionAsync()
    {
        QrSessionEnvelope? currentContext = context;
        if (currentContext?.Session is not QrDiningSessionResponse session)
        {
            return;
        }

        try
        {
            if (ShouldRenewQrReservation(session))
            {
                await RenewQrReservationAsync(session);
                if (context is null)
                {
                    return;
                }
            }
            else if (!await RefreshQrPreviewAsync(currentContext))
            {
                return;
            }

            StateHasChanged();
        }
        catch (CustomerApiException exception) when (exception.StatusCode == HttpStatusCode.Gone)
        {
            await ExpireCustomerSessionAsync(GetCustomerSessionStorageKey());
        }
    }

    private bool ShouldRenewQrReservation(QrDiningSessionResponse session) =>
        !qrOrderSubmitted
        && session.QrReservationExpiresAtUtc is not null
        && lastQrActivityAtUtc > lastQrTouchAtUtc;

    private async Task<bool> RefreshQrPreviewAsync(QrSessionEnvelope currentContext)
    {
        context = await Api.PreviewAsync(QrCode, currentContext.CustomerAccessToken);
        if (context.Session is not null)
        {
            qrOrderSubmitted = context.Session.QrReservationExpiresAtUtc is null;
            return true;
        }

        await ExpireCustomerSessionAsync(GetCustomerSessionStorageKey());
        return false;
    }

    private string GetCustomerSessionStorageKey() =>
        $"restaurantes.qr.{QrCode.Trim().ToUpperInvariant()}";

    private async Task ExpireCustomerSessionAsync(string storageKey)
    {
        await StopPollingAsync();
        await Js.InvokeVoidAsync("localStorage.removeItem", storageKey);
        context = null;
        quantities.Clear();
        notes.Clear();
        pendingOrder = null;
        pendingPaymentKey = Guid.Empty;
        customerOrders = [];
        success = string.Empty;
        sessionExpired = true;
        error = string.Empty;
        StateHasChanged();
    }

    private async Task StopPollingAsync()
    {
        if (statusPolling is not null)
        {
            await statusPolling.CancelAsync();
            statusPolling.Dispose();
            statusPolling = null;
        }

        if (sessionPolling is not null)
        {
            await sessionPolling.CancelAsync();
            sessionPolling.Dispose();
            sessionPolling = null;
        }
    }

    private async Task PollOrderStatusesAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    await RefreshOrderStatusesAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshOrderStatusesAsync()
    {
        if (context?.Session is null || customerOrders.Count == 0)
        {
            return;
        }
        List<OrderResponse> refreshed = [];
        foreach (OrderResponse known in customerOrders)
        {
            try
            {
                refreshed.Add(await Api.OrderAsync(known.Id, context.CustomerAccessToken));
            }
            catch (CustomerApiException exception)
                when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                refreshed.Add(known);
            }
        }
        customerOrders = refreshed;
    }

    private async Task PersistOrderIdsAsync()
    {
        if (string.IsNullOrWhiteSpace(ordersStorageKey))
        {
            return;
        }
        await Js.InvokeVoidAsync(
            "localStorage.setItem",
            ordersStorageKey,
            JsonSerializer.Serialize(customerOrders.Select(x => x.Id))
        );
    }

    public ValueTask DisposeAsync()
    {
        statusPolling?.Cancel();
        statusPolling?.Dispose();
        sessionPolling?.Cancel();
        sessionPolling?.Dispose();
        CatalogRealtime.Changed -= HandleCatalogChanged;
        return CatalogRealtime.DisposeAsync();
    }

    private Task HandleCatalogChanged(Guid changedRestaurantId)
    {
        if (
            context is null
            || (
                changedRestaurantId != Guid.Empty
                && changedRestaurantId != context.Table.RestaurantId
            )
        )
            return Task.CompletedTask;

        return InvokeAsync(async () =>
        {
            menu = await Api.MenuAsync(context.Table.RestaurantId);
            StateHasChanged();
        });
    }

    private static async Task<T> RetryAsync<T>(
        Func<Task<T>> action,
        Func<HttpStatusCode, bool> shouldRetry
    )
    {
        const int attempts = 30;
        for (int attempt = 1; attempt < attempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (CustomerApiException exception) when (shouldRetry(exception.StatusCode))
            {
                await Task.Delay(500);
            }
        }

        return await action();
    }
}
