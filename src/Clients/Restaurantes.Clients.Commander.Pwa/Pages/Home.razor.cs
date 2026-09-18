using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Commander.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Commander.Pwa.Pages;

public partial class Home
{
    private int? guestCount;
    private DiningSessionResponse? diningSession;
    private const string SessionKey = "restaurantes.commander.login";
    private string? message;
    private bool busy;
    private bool success;
    private LoginResponse? login;
    private ProviderSettingsResponse? providerSettings;
    private Guid restaurantId;

    private List<TableResponse> FilteredTables { get; set; } = [];
    private List<OrderResponse> activeOrders = [];
    private TableResponse? selectedTable;
    private Guid sessionId;
    private List<MenuItemResponse> menu = [];
    private string? category;
    private Dictionary<Guid, int> quantities = [];
    private Dictionary<Guid, string> notes = [];
    private SessionBillResponse? bill;
    private bool billOpen;
    private string paymentMethod = "Card";
    private string paymentReference = string.Empty;
    private Guid checkoutKey;
    private Dictionary<Guid, OrderDetailResponse> billOrders = [];
    private CancellationTokenSource? realtimeRefresh;

    private int ItemCount => quantities.Values.Sum();
    private decimal Total =>
        menu.Sum(item => item.Price * quantities.GetValueOrDefault(item.ProductId));
    private List<string> Categories =>
        menu.Where(x => x.IsAvailable).Select(x => x.CategoryName).Distinct().Order().ToList();
    private List<MenuItemResponse> VisibleMenu =>
        menu.Where(x => x.IsAvailable && (category is null || x.CategoryName == category))
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        Realtime.OrderUpdated += HandleOrderUpdated;
        DiningRealtime.TableChanged += HandleTableChanged;
        providerSettings = await Api.ProviderAsync();
        string? code = QueryValue("login_code");
        if (!string.IsNullOrWhiteSpace(code))
        {
            Nav.NavigateTo(Nav.BaseUri, replace: true);
            await Run(async () => await AcceptLogin(await Api.ExchangeAsync(code)));
            return;
        }
        if (QueryValue("login_error") is not null)
        {
            message = "La cuenta no está autorizada o el proveedor rechazó el acceso.";
        }

        string? json = await Js.InvokeAsync<string?>("sessionStorage.getItem", SessionKey);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                login = JsonSerializer.Deserialize<LoginResponse>(json);
                if (
                    login is not null
                    && login.ExpiresAtUtc > DateTime.UtcNow
                    && login.Restaurants.Count > 0
                    && login.Restaurants.Any(x => x.Permissions.Contains("payments.capture"))
                )
                {
                    Api.AccessToken = login.AccessToken;
                    restaurantId = login.Restaurants[0].RestaurantId;
                    await LoadTables();
                    await ConnectRealtimeAsync();
                }
                else
                {
                    login = null;
                    await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
                }
            }
            catch (JsonException)
            {
                login = null;
            }
        }
    }

    private void StartLogin(string provider)
    {
        Nav.NavigateTo(
            $"/api/identity/auth/start/{provider}?returnPath=%2Fcommander%2F",
            forceLoad: true
        );
    }

    private string? QueryValue(string key)
    {
        string query = new Uri(Nav.Uri).Fragment.TrimStart('#');
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair[0] == key)
            {
                return Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : "");
            }
        }
        return null;
    }

    private async Task AcceptLogin(LoginResponse response)
    {
        login = response;
        if (login.Restaurants.Count == 0)
        {
            throw new InvalidOperationException("El usuario no tiene ningún local asignado.");
        }

        Api.AccessToken = login.AccessToken;
        restaurantId = login.Restaurants[0].RestaurantId;
        await Js.InvokeVoidAsync(
            "sessionStorage.setItem",
            SessionKey,
            JsonSerializer.Serialize(login)
        );
        await LoadOperationalData();
        await ConnectRealtimeAsync();
    }

    private async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        await Realtime.DisconnectAsync();
        await DiningRealtime.DisconnectAsync();
        Api.AccessToken = null;
        login = null;
        FilteredTables = [];
        BackToTables();
        message = null;
    }

    private async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out Guid value))
        {
            restaurantId = value;
            BackToTables();
            await LoadTables();
            await ConnectRealtimeAsync();
        }
    }

    private async Task LoadTables()
    {
        await Run(LoadOperationalData);
    }

    private async Task LoadOperationalData()
    {
        FilteredTables = await Api.TablesAsync(restaurantId);
        activeOrders = await Api.OrdersAsync(restaurantId);
    }

    private async Task SelectTable(TableResponse table)
    {
        await Run(async () =>
        {
            DiningSessionResponse session = table.ActiveSessionId.HasValue
                ? await Api.ActiveSessionAsync(table.Id)
                : await Api.OpenSessionAsync(table.Id);
            selectedTable = table;
            sessionId = session.Id;
            diningSession = session;
            guestCount = session.GuestCount;
            menu = await Api.MenuAsync(restaurantId);
            category = null;
            quantities = menu.ToDictionary(x => x.ProductId, _ => 0);
            notes = menu.ToDictionary(x => x.ProductId, _ => string.Empty);
        });
    }

    private void ChangeQuantity(Guid productId, int change)
    {
        quantities[productId] = Math.Clamp(quantities[productId] + change, 0, 99);
    }

    private async Task SendOrder()
    {
        if (selectedTable is null)
        {
            return;
        }

        await Run(
            async () =>
            {
                if (diningSession is { RequestGuestCount: true, GuestCount: null })
                {
                    diningSession = await Api.SetGuestCountAsync(sessionId, guestCount ?? 0);
                }

                OrderResponse order = await Api.CreateAndSubmitAsync(
                    restaurantId,
                    selectedTable,
                    sessionId,
                    quantities,
                    notes
                );
                foreach (Guid id in quantities.Keys.ToArray())
                {
                    quantities[id] = 0;
                    notes[id] = string.Empty;
                }
                activeOrders = await Api.OrdersAsync(restaurantId);
                success = true;
                message = $"Pedido {order.Id.ToString()[..8]} enviado a cocina.";
            },
            clearMessage: false
        );
    }

    private async Task OpenBill()
    {
        billOpen = true;
        await Run(LoadBill);
    }

    private async Task LoadBill()
    {
        bill = await Api.BillAsync(sessionId);
        OrderDetailResponse[] details = await Task.WhenAll(
            bill.Orders.Select(order => Api.OrderAsync(order.OrderId))
        );
        billOrders = details.ToDictionary(order => order.Id);
    }

    private static bool CanCancelLine(
        SessionBillOrderResponse billOrder,
        OrderDetailResponse order,
        OrderLineDetailResponse line
    )
    {
        return billOrder.PaymentStatus != "Paid"
            && line.Status == "Active"
            && OrderProgress.CanCancelBeforePreparation(order.Status)
            && order.Stations.Any(station =>
                station.Code == line.PreparationStationCode && station.Status == "Pending"
            );
    }

    private async Task CancelLine(SessionBillOrderResponse order, OrderLineDetailResponse line)
    {
        string? reason = await Js.InvokeAsync<string?>(
            "prompt",
            $"Motivo para cancelar {line.Quantity} × {line.ProductName}:"
        );
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        bool confirmed = await Js.InvokeAsync<bool>(
            "confirm",
            "La línea desaparecerá del KDS si cocina todavía no comenzó. ¿Continuar?"
        );
        if (!confirmed)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.CancelOrderLineAsync(order.OrderId, line.Id, reason.Trim());
                await Task.Delay(500);
                await LoadBill();
                success = true;
                message = $"{line.ProductName} cancelado antes de preparación.";
            },
            clearMessage: false
        );
    }

    private void CloseBill()
    {
        billOpen = false;
    }

    private async Task CheckoutSession()
    {
        if (busy)
        {
            return;
        }

        checkoutKey = checkoutKey == Guid.Empty ? Guid.NewGuid() : checkoutKey;
        await Run(
            async () =>
            {
                bill = await Api.CheckoutAsync(
                    sessionId,
                    checkoutKey,
                    paymentMethod,
                    paymentReference
                );
                success = true;
                message = $"Cuenta cobrada: {bill.Total:0.00} €. Mesa liberada.";
                checkoutKey = Guid.Empty;
                billOpen = false;
                BackToTables();
                await LoadOperationalData();
            },
            clearMessage: false
        );
    }

    private async Task ReleaseSession()
    {
        await Run(
            async () =>
            {
                bool emptySession = bill?.Orders.Count == 0;
                if (emptySession)
                {
                    await Api.CancelSessionAsync(sessionId, "Apertura sin pedidos");
                }
                else
                {
                    await Api.ReleaseSessionAsync(sessionId);
                }
                success = true;
                message = emptySession
                    ? "Apertura sin pedidos cancelada y ubicación liberada."
                    : "Ubicación liberada por el camarero.";
                billOpen = false;
                BackToTables();
                await LoadOperationalData();
            },
            clearMessage: false
        );
    }

    private void BackToTables()
    {
        selectedTable = null;
        sessionId = Guid.Empty;
        menu = [];
        category = null;
        quantities = [];
        notes = [];
        bill = null;
        billOrders = [];
        billOpen = false;
        checkoutKey = Guid.Empty;
        paymentReference = string.Empty;
    }

    private string CategoryClass(string? value)
    {
        return category == value ? "category-filter active" : "category-filter";
    }

    private string TableOrderSummary(TableResponse table)
    {
        return OrderProgress.Summarize(
            activeOrders.Where(order => order.TableId == table.Id).Select(order => order.Status)
        );
    }

    private string SessionOrderSummary(Guid currentSessionId)
    {
        return OrderProgress.Summarize(
            activeOrders
                .Where(order => order.DiningSessionId == currentSessionId)
                .Select(order => order.Status)
        );
    }

    private async Task Run(Func<Task> action, bool clearMessage = true)
    {
        if (clearMessage)
        {
            message = null;
        }

        success = false;
        busy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            message = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    private async Task ConnectRealtimeAsync()
    {
        try
        {
            await Realtime.ConnectAsync(restaurantId);
            await DiningRealtime.ConnectAsync(restaurantId);
        }
        catch (Exception exception)
        {
            message = $"Datos cargados, pero sin actualización en tiempo real: {exception.Message}";
        }
    }

    private Task HandleOrderUpdated(OrderRealtimeNotification notification)
    {
        return ScheduleRealtimeRefresh(notification.RestaurantId);
    }

    private Task HandleTableChanged(DiningTableChangedNotification notification)
    {
        return ScheduleRealtimeRefresh(notification.RestaurantId);
    }

    private Task ScheduleRealtimeRefresh(Guid changedRestaurantId)
    {
        if (login is null || changedRestaurantId != restaurantId)
        {
            return Task.CompletedTask;
        }

        realtimeRefresh?.Cancel();
        realtimeRefresh?.Dispose();
        realtimeRefresh = new CancellationTokenSource();
        _ = RefreshAfterOrderEventAsync(realtimeRefresh.Token);
        return Task.CompletedTask;
    }

    private async Task RefreshAfterOrderEventAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await InvokeAsync(async () =>
            {
                FilteredTables = await Api.TablesAsync(restaurantId);
                activeOrders = await Api.OrdersAsync(restaurantId);
                if (selectedTable is not null)
                {
                    TableResponse? current = FilteredTables.FirstOrDefault(x =>
                        x.Id == selectedTable.Id
                    );
                    if (current?.ActiveSessionId == sessionId)
                    {
                        selectedTable = current;
                        if (billOpen)
                        {
                            await LoadBill();
                        }
                    }
                    else
                    {
                        BackToTables();
                    }
                }
                StateHasChanged();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
            {
                message = $"No se pudo actualizar el comandero en tiempo real: {exception.Message}";
                StateHasChanged();
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        Realtime.OrderUpdated -= HandleOrderUpdated;
        DiningRealtime.TableChanged -= HandleTableChanged;
        realtimeRefresh?.Cancel();
        realtimeRefresh?.Dispose();
        await Realtime.DisconnectAsync();
        await DiningRealtime.DisconnectAsync();
    }
}
