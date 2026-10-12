using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Commander.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Commander.Pwa.Pages;

public partial class Home
{
    private int? guestCount;
    private int guestCountDraft = 2;
    private bool guestCountDialogOpen;
    private bool guestCountSessionOpenedHere;
    private TableResponse? guestCountTable;
    private DiningSessionResponse? diningSession;
    private const string SessionKey = "restaurantes.commander.login";
    private string? message;
    private bool busy;
    private bool success;
    private LoginResponse? login;
    private bool initializing = true;
    private ProviderSettingsResponse? providerSettings;
    private Guid restaurantId;
    private Guid? selectedZoneId;
    private List<RestaurantResponse> restaurants = [];

    private List<TableResponse> FilteredTables { get; set; } = [];
    private IEnumerable<TableResponse> TablesInSelectedZone =>
        selectedZoneId is Guid zoneId
            ? FilteredTables
                .Where(table => table.ZoneId == zoneId && table.IsActive)
                .OrderBy(table => table.Code)
            : [];
    private string SelectedZoneName =>
        FilteredTables.FirstOrDefault(table => table.ZoneId == selectedZoneId)?.ZoneName ?? "";
    private List<OrderResponse> activeOrders = [];
    private TableResponse? selectedTable;
    private Guid sessionId;
    private List<MenuItemResponse> menu = [];
    private string? category;
    private List<CommanderCartLine> cart = [];
    private Dictionary<Guid, string> notes = [];
    private MenuItemResponse? extraDialogProduct;
    private Guid? editingExtraLineId;
    private Dictionary<(Guid GroupId, Guid ProductId), int> extraSelections = [];
    private SessionBillResponse? bill;
    private bool billOpen;
    private Dictionary<Guid, OrderDetailResponse> billOrders = [];
    private CancellationTokenSource? realtimeRefresh;

    private int ItemCount => cart.Sum(line => line.Quantity);
    private decimal Total => cart.Sum(line => UnitPrice(line) * line.Quantity);
    private List<string> Categories =>
        menu.Where(x => x.IsAvailable).Select(x => x.CategoryName).Distinct().Order().ToList();
    private List<MenuItemResponse> VisibleMenu =>
        menu.Where(x => x.IsAvailable && (category is null || x.CategoryName == category))
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .ToList();
    private bool CanShowCancelOwnSession =>
        diningSession is not null && diningSession.OpenedByUserId == login?.User.Id;
    private bool CanReleaseCancelledSession =>
        CanShowCancelOwnSession
        && bill is { Status: "Open", Outstanding: 0 }
        && bill.Orders.Count > 0
        && bill.Orders.All(order =>
            order.OrderStatus == "Cancelled" && order.PaymentStatus != "Paid" && order.Amount == 0
        );
    private bool HasSessionOrders
    {
        get => field || activeOrders.Any(order => order.DiningSessionId == sessionId);
        set;
    }

    protected override async Task OnInitializedAsync()
    {
        Realtime.OrderUpdated += HandleOrderUpdated;
        try
        {
            providerSettings = await Api.ProviderAsync();
            string? code = QueryValue("login_code");
            if (!string.IsNullOrWhiteSpace(code))
            {
                await Js.InvokeVoidAsync("history.replaceState", null, "", Nav.BaseUri);
                await AcceptLogin(await Api.ExchangeAsync(code));
                return;
            }
            if (QueryValue("login_error") is not null)
            {
                await Js.InvokeVoidAsync("history.replaceState", null, "", Nav.BaseUri);
                message = "La cuenta no está autorizada o el proveedor rechazó el acceso.";
            }

            string? json = await Js.InvokeAsync<string?>("sessionStorage.getItem", SessionKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                login = JsonSerializer.Deserialize<LoginResponse>(json);
                if (login is not null && login.ExpiresAtUtc > DateTime.UtcNow)
                {
                    Api.AccessToken = login.AccessToken;
                    await LoadRestaurants();
                    await LoadTables();
                    await ConnectRealtimeAsync();
                }
                else
                {
                    login = null;
                    await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
                }
            }
        }
        catch (Exception exception)
        {
            message = exception.Message;
        }
        finally
        {
            initializing = false;
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
        Api.AccessToken = login.AccessToken;
        await Js.InvokeVoidAsync(
            "sessionStorage.setItem",
            SessionKey,
            JsonSerializer.Serialize(login)
        );
        await LoadRestaurants();
        await LoadOperationalData();
        await ConnectRealtimeAsync();
    }

    private async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        await Realtime.DisconnectAsync();
        Api.AccessToken = null;
        login = null;
        restaurants = [];
        FilteredTables = [];
        selectedZoneId = null;
        BackToTables();
        message = null;
    }

    private async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out Guid value))
        {
            restaurantId = value;
            selectedZoneId = null;
            BackToTables();
            await LoadTables();
            await ConnectRealtimeAsync();
        }
    }

    private async Task LoadTables()
    {
        await Run(LoadOperationalData);
    }

    private async Task LoadRestaurants()
    {
        restaurants = await Api.RestaurantsAsync();
        if (restaurants.Count == 0)
        {
            throw new InvalidOperationException(
                "El usuario no tiene permisos para operar el Comandero en ningún local."
            );
        }

        if (!restaurants.Any(restaurant => restaurant.Id == restaurantId))
        {
            restaurantId = restaurants[0].Id;
        }
    }

    private async Task LoadOperationalData()
    {
        FilteredTables = (await Api.TablesAsync(restaurantId))
            .Where(table => table.IsActive)
            .ToList();
        if (selectedZoneId is Guid zoneId && FilteredTables.All(table => table.ZoneId != zoneId))
        {
            selectedZoneId = null;
        }

        activeOrders = await Api.OrdersAsync(restaurantId);
    }

    private void SelectZone(Guid zoneId)
    {
        selectedZoneId = zoneId;
    }

    private void BackToZones()
    {
        selectedZoneId = null;
    }

    private async Task SelectTable(TableResponse table)
    {
        await Run(async () =>
        {
            guestCountSessionOpenedHere = !table.ActiveSessionId.HasValue;
            DiningSessionResponse session = table.ActiveSessionId.HasValue
                ? await Api.ActiveSessionAsync(table.Id)
                : await Api.OpenSessionAsync(table.Id);
            sessionId = session.Id;
            diningSession = session;
            HasSessionOrders = activeOrders.Any(order => order.DiningSessionId == sessionId);
            guestCount = session.GuestCount;
            guestCountDraft = guestCount ?? 2;
            guestCountDialogOpen = session.RequestGuestCount && session.GuestCount is null;
            guestCountTable = guestCountDialogOpen ? table : null;
            selectedTable = guestCountDialogOpen ? null : table;
            menu = guestCountDialogOpen ? [] : await Api.MenuAsync(restaurantId);
            category = null;
            notes = menu.ToDictionary(x => x.ProductId, _ => string.Empty);
        });
    }

    private async Task AcceptGuestCount()
    {
        if (
            diningSession is null
            || guestCountTable is null
            || guestCountDraft is < 1 or > 999
            || busy
        )
        {
            return;
        }

        await Run(async () =>
        {
            diningSession = await Api.SetGuestCountAsync(sessionId, guestCountDraft);
            guestCount = diningSession.GuestCount;
            menu = await Api.MenuAsync(restaurantId);
            selectedTable = guestCountTable;
            guestCountTable = null;
            guestCountDialogOpen = false;
            guestCountSessionOpenedHere = false;
            notes = menu.ToDictionary(x => x.ProductId, _ => string.Empty);
        });
    }

    private async Task CancelGuestCount()
    {
        if (guestCountSessionOpenedHere && diningSession is not null && !HasSessionOrders)
        {
            await Run(async () =>
            {
                await Api.CancelSessionAsync(diningSession.Id);
                BackToTables();
                await LoadOperationalData();
            });
            return;
        }

        BackToTables();
    }

    private void ChangeQuantity(Guid productId, int change)
    {
        MenuItemResponse? product = menu.FirstOrDefault(item => item.ProductId == productId);
        if (product is null)
        {
            return;
        }

        if (change > 0 && product.ExtraGroups is { Count: > 0 })
        {
            extraDialogProduct = product;
            editingExtraLineId = null;
            extraSelections = [];
            return;
        }

        CommanderCartLine? line = cart.LastOrDefault(item => item.ProductId == productId);
        if (line is null)
        {
            if (change > 0)
            {
                cart.Add(new CommanderCartLine { ProductId = productId });
            }

            return;
        }

        line.Quantity = Math.Clamp(line.Quantity + change, 0, 99);
        if (line.Quantity == 0)
        {
            cart.Remove(line);
        }
    }

    private int ProductQuantity(Guid productId) =>
        cart.Where(line => line.ProductId == productId).Sum(line => line.Quantity);

    private MenuItemResponse ProductFor(CommanderCartLine line) =>
        menu.First(item => item.ProductId == line.ProductId);

    private decimal UnitPrice(CommanderCartLine line)
    {
        MenuItemResponse product = ProductFor(line);
        return product.Price
            + line.Extras.Sum(extra =>
                product
                    .ExtraGroups?.FirstOrDefault(group => group.Id == extra.GroupId)
                    ?.Options.FirstOrDefault(option => option.ProductId == extra.ProductId)
                    ?.Price * extra.Quantity
                ?? 0
            );
    }

    private void EditExtras(CommanderCartLine line)
    {
        extraDialogProduct = ProductFor(line);
        editingExtraLineId = line.Id;
        extraSelections = line.Extras.ToDictionary(
            extra => (extra.GroupId, extra.ProductId),
            extra => extra.Quantity
        );
    }

    private int ExtraQuantity(ProductExtraGroup group, ProductExtraOption option) =>
        extraSelections.GetValueOrDefault((group.Id, option.ProductId));

    private int GroupExtraQuantity(ProductExtraGroup group) =>
        extraSelections
            .Where(selection => selection.Key.GroupId == group.Id)
            .Sum(selection => selection.Value);

    private void ChangeExtraQuantity(ProductExtraGroup group, ProductExtraOption option, int change)
    {
        var key = (group.Id, option.ProductId);
        int current = extraSelections.GetValueOrDefault(key);
        int next = Math.Clamp(current + change, 0, 99);
        if (GroupExtraQuantity(group) + next - current > group.MaxSelections)
        {
            return;
        }

        if (next == 0)
        {
            extraSelections.Remove(key);
        }
        else
        {
            extraSelections[key] = next;
        }
    }

    private bool CanAcceptExtras =>
        extraDialogProduct?.ExtraGroups?.All(group =>
            GroupExtraQuantity(group) >= group.MinSelections
            && GroupExtraQuantity(group) <= group.MaxSelections
        )
        ?? false;

    private void AcceptExtras()
    {
        if (extraDialogProduct is null || !CanAcceptExtras)
        {
            return;
        }

        List<CommanderCartExtra> selections = extraSelections
            .Where(selection => selection.Value > 0)
            .Select(selection => new CommanderCartExtra(
                selection.Key.GroupId,
                selection.Key.ProductId,
                selection.Value
            ))
            .ToList();
        if (editingExtraLineId is Guid lineId)
        {
            CommanderCartLine? line = cart.FirstOrDefault(item => item.Id == lineId);
            if (line is not null)
            {
                line.Extras.Clear();
                line.Extras.AddRange(selections);
            }
        }
        else
        {
            cart.Add(
                new CommanderCartLine
                {
                    ProductId = extraDialogProduct.ProductId,
                    Extras = selections,
                }
            );
        }

        CloseExtras();
    }

    private void CloseExtras()
    {
        extraDialogProduct = null;
        editingExtraLineId = null;
        extraSelections = [];
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
                    cart,
                    notes
                );
                HasSessionOrders = true;
                cart = [];
                foreach (Guid id in notes.Keys.ToArray())
                    notes[id] = string.Empty;
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

    private async Task CancelSession()
    {
        if (diningSession is null)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.CancelSessionAsync(diningSession.Id);
                success = true;
                message = "Apertura cancelada y ubicación liberada.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
    }

    private async Task ReleaseCancelledSession()
    {
        if (diningSession is null || !CanReleaseCancelledSession)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.CloseSessionAsync(diningSession.Id);
                success = true;
                message = "Mesa liberada. Las comandas canceladas se conservan en el historial.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
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
            && line.KitchenStatus == "Pending";
    }

    private static string KitchenLineStatusLabel(string status) =>
        status switch
        {
            "InPreparation" => "Preparando",
            "Ready" => "Esperando chef",
            "Dispatched" => "Despachado",
            _ => "Pendiente",
        };

    private static string KitchenLineStatusClass(string status) =>
        status switch
        {
            "InPreparation" => "preparing",
            "Ready" => "awaiting-dispatch",
            "Dispatched" => "dispatched",
            _ => "pending",
        };

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

        await Run(async () =>
        {
            await Api.CancelOrderLineAsync(order.OrderId, line.Id, reason.Trim());
            await Task.Delay(500);
            await LoadBill();
        });
    }

    private void CloseBill()
    {
        billOpen = false;
    }

    private void BackToTables()
    {
        guestCountDialogOpen = false;
        guestCountSessionOpenedHere = false;
        guestCountTable = null;
        selectedTable = null;
        sessionId = Guid.Empty;
        diningSession = null;
        HasSessionOrders = false;
        menu = [];
        category = null;
        cart = [];
        notes = [];
        CloseExtras();
        bill = null;
        billOrders = [];
        billOpen = false;
    }

    private string CategoryClass(string? value)
    {
        return category == value ? "category-filter active" : "category-filter";
    }

    private string TableOrderSummary(TableResponse table)
    {
        return table.ActiveSessionId is not Guid sessionId
            ? "Sin comandas activas"
            : OrderProgress.Summarize(
                activeOrders
                    .Where(order => order.TableId == table.Id && order.DiningSessionId == sessionId)
                    .Select(order => order.Status)
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
                FilteredTables = (await Api.TablesAsync(restaurantId))
                    .Where(table => table.IsActive)
                    .ToList();
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
        if (realtimeRefresh is not null)
        {
            await realtimeRefresh.CancelAsync();
        }

        realtimeRefresh?.Dispose();
        await Realtime.DisconnectAsync();
    }
}
