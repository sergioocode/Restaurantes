using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.CustomerQr.Pwa.Api;
using Restaurantes.Clients.CustomerQr.Pwa.Models;

namespace Restaurantes.Clients.CustomerQr.Pwa.Pages;

public partial class QrOrder
{
    int? guestCount;
    string? guestError;

    [Parameter]
    public string QrCode { get; set; } = string.Empty;

    QrSessionEnvelope? context;
    RestaurantResponse? restaurant;
    List<MenuItemResponse> menu = [];
    readonly Dictionary<Guid, int> quantities = [];
    readonly Dictionary<Guid, string> notes = [];
    bool loading = true;
    bool busy;
    string error = string.Empty;
    string success = string.Empty;
    string progress = string.Empty;
    OrderResponse? pendingOrder;
    Guid pendingPaymentKey;
    List<OrderResponse> customerOrders = [];
    CancellationTokenSource? statusPolling;
    string ordersStorageKey = string.Empty;

    IEnumerable<IGrouping<string, MenuItemResponse>> Categories =>
        menu.Where(x => x.IsAvailable)
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .GroupBy(x => x.CategoryName);

    bool NeedsGuestCount =>
        context is not null
        && (
            context.Session is null
                ? context.Table.RequestGuestCount
                : context.Session.RequestGuestCount && context.Session.GuestCount is null
        );

    int ItemCount => quantities.Values.Sum();
    decimal Total => menu.Sum(x => x.Price * Quantity(x.ProductId));

    protected override Task OnParametersSetAsync() => LoadAsync();

    async Task LoadAsync()
    {
        statusPolling?.Cancel();
        loading = true;
        error = string.Empty;
        success = string.Empty;
        try
        {
            string storageKey = $"restaurantes.qr.{QrCode.Trim().ToUpperInvariant()}";
            string? savedToken = await Js.InvokeAsync<string?>("localStorage.getItem", storageKey);
            Guid? previousSessionId = context?.Session?.Id;
            Guid? previousTableId = context?.Table.Id;
            context = await Api.PreviewAsync(QrCode, savedToken);
            if (previousTableId.HasValue && previousTableId != context.Table.Id)
            {
                quantities.Clear();
                notes.Clear();
                guestCount = null;
            }
            if (previousSessionId != context.Session?.Id)
            {
                pendingOrder = null;
                pendingPaymentKey = Guid.Empty;
                guestCount = null;
            }
            guestCount = context.Session?.GuestCount ?? guestCount;

            restaurant = await Api.RestaurantAsync(context.Table.RestaurantId);
            menu = await Api.MenuAsync(context.Table.RestaurantId);
            customerOrders = [];
            ordersStorageKey = string.Empty;
            if (context.Session is null)
            {
                return;
            }
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
            await RefreshOrderStatusesAsync();
            StartStatusPolling();
        }
        catch (CustomerApiException exception)
        {
            error =
                exception.StatusCode == HttpStatusCode.Forbidden
                    ? "Conéctate a la red autorizada del restaurante y vuelve a escanear el QR."
                    : exception.Message;
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

    int Quantity(Guid productId) => quantities.GetValueOrDefault(productId);

    string Note(Guid productId) => notes.GetValueOrDefault(productId) ?? string.Empty;

    void Change(Guid productId, int delta)
    {
        quantities[productId] = Math.Clamp(Quantity(productId) + delta, 0, 100);
    }

    void SetNote(Guid productId, string? value)
    {
        notes[productId] = value ?? string.Empty;
    }

    async Task SubmitOrderAsync()
    {
        if (context is null || ItemCount == 0)
        {
            return;
        }

        guestError = null;
        if (NeedsGuestCount && guestCount is null or < 1 or > 999)
        {
            guestError = "Indica entre 1 y 999 comensales.";
            return;
        }
        busy = true;
        error = string.Empty;
        success = string.Empty;
        try
        {
            progress = "Validando presencia…";
            Guid? previousSessionId = context.Session?.Id;
            context = await Api.OpenSessionAsync(QrCode, context.CustomerAccessToken, guestCount);
            if (context.Session is null)
            {
                throw new InvalidOperationException("No se pudo abrir la sesión.");
            }
            await Js.InvokeVoidAsync(
                "localStorage.setItem",
                $"restaurantes.qr.{QrCode.Trim().ToUpperInvariant()}",
                context.CustomerAccessToken
            );
            if (previousSessionId != context.Session.Id)
            {
                pendingOrder = null;
                pendingPaymentKey = Guid.Empty;
                customerOrders = [];
            }
            ordersStorageKey = $"restaurantes.qr.orders.{context.Session.Id:N}";
            if (NeedsGuestCount)
            {
                context = context with
                {
                    Session = await Api.SetGuestCountAsync(
                        context.Session.Id,
                        guestCount ?? 0,
                        context.CustomerAccessToken
                    ),
                };
            }

            OrderResponse order;
            if (pendingOrder is null)
            {
                progress = "Creando pedido…";
                pendingOrder = await Api.CreateOrderAsync(context, quantities, notes);
                pendingPaymentKey = Guid.NewGuid();
            }
            order = pendingOrder;

            if (context.QrRequiresImmediatePayment)
            {
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
            }

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
            await PersistOrderIdsAsync();
            StartStatusPolling();
            success = context.QrRequiresImmediatePayment
                ? $"Pedido {submitted.Id.ToString()[..8]} · {submitted.Total:0.00} € pagado y enviado."
                : $"Pedido {submitted.Id.ToString()[..8]} · {submitted.Total:0.00} €. Se añadirá a la cuenta de tu mesa.";
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            busy = false;
            progress = string.Empty;
        }
    }

    void StartStatusPolling()
    {
        statusPolling?.Cancel();
        statusPolling?.Dispose();
        statusPolling = new CancellationTokenSource();
        _ = PollOrderStatusesAsync(statusPolling.Token);
    }

    async Task PollOrderStatusesAsync(CancellationToken cancellationToken)
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

    async Task RefreshOrderStatusesAsync()
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

    async Task PersistOrderIdsAsync()
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
        return ValueTask.CompletedTask;
    }

    static async Task<T> RetryAsync<T>(Func<Task<T>> action, Func<HttpStatusCode, bool> shouldRetry)
    {
        const int attempts = 30;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (CustomerApiException exception)
                when (attempt < attempts && shouldRetry(exception.StatusCode))
            {
                await Task.Delay(500);
            }
        }
    }
}
