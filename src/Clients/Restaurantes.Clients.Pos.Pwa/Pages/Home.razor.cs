using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Pos.Pwa.Pages;

public partial class Home
{
    private int? guestCount;
    private int guestCountDraft = 2;
    private bool guestCountDialogOpen;
    private bool guestCountSessionOpenedHere;
    private const string SessionKey = "restaurantes.pos.login";
    private string? message;
    private bool busy;
    private bool success;
    private bool initializing = true;
    private LoginResponse? login;
    private ProviderSettingsResponse? providerSettings;
    private Guid restaurantId;
    private List<RestaurantResponse> restaurants = [];
    private List<TableResponse> tables = [];
    private Guid? selectedZoneId;
    private string tableFilter = "All";
    private bool pickupView;
    private string serviceMode = "DineIn";
    private string customerName = string.Empty;
    private bool takeawayOpen;
    private List<OrderResponse> activeOrders = [];
    private List<OrderResponse> pickupOrders = [];
    private OrderResponse? selectedPickup;
    private OrderDetailResponse? pickupDetail;
    private Dictionary<Guid, string> pickupPaymentStatuses = [];
    private string pickupPaymentStatus = "ProjectionPending";
    private Guid pickupPaymentKey;
    private bool pickupBillOpen;
    private TableResponse? selectedTable;
    private TableResponse? guestCountTable;
    private DiningSessionResponse? session;
    private List<MenuItemResponse> menu = [];
    private List<PosCartLine> cart = [];
    private MenuItemResponse? extraDialogProduct;
    private Guid? editingExtraLineId;
    private Dictionary<(Guid GroupId, Guid ProductId), int> extraSelections = [];
    private string? category;
    private SessionBillResponse? bill;
    private string? billLoadError;
    private bool billOpen;
    private bool paymentDialogOpen;
    private string keypadTarget = "Charge";
    private string keypadText = string.Empty;
    private bool keypadStartNew = true;
    private decimal? lastChangeAmount;
    private string paymentMethod = "Card";
    private string paymentReference = string.Empty;
    private decimal currentChargeAmount;
    private int tipPercentage;
    private decimal tipAmount;
    private bool tipAmountManuallyEdited;
    private decimal CurrentTipAmount => tipAmount;
    private decimal SuggestedTipAmount =>
        Math.Round(
            Math.Min(currentChargeAmount, bill?.Outstanding ?? 0) * tipPercentage / 100m,
            2,
            MidpointRounding.AwayFromZero
        );
    private PosPaymentPreview PaymentPreview =>
        PosPaymentPreview.Calculate(bill?.Outstanding ?? 0, currentChargeAmount, CurrentTipAmount);
    private decimal AmountReceived => PaymentPreview.Received;
    private decimal ChangeAmount => PaymentPreview.Change;
    private decimal RemainingAfterPayment => PaymentPreview.Remaining;
    private bool IsChargeAmountValid =>
        bill is not null
        && (paymentMethod is "Cash" or "Card" or "Transfer")
        && currentChargeAmount > 0
        && AmountReceived <= 1_000_000m
        && (paymentMethod != "Transfer" || currentChargeAmount == bill.Outstanding);
    private Guid checkoutKey;
    private Dictionary<Guid, OrderDetailResponse> billOrders = [];
    private CancellationTokenSource? realtimeRefresh;
    private CancellationTokenSource? reconciliation;
    private CancellationTokenSource? kitchenClockRefresh;
    private CashRegisterResponse? cashRegister;
    private bool cashPanelOpen;
    private decimal openingFloat;
    private Dictionary<string, decimal> reconciliationAmounts = new(
        StringComparer.OrdinalIgnoreCase
    );

    private bool CashRegisterOpen => cashRegister?.Status == "Open";
    private decimal ReconciliationDifference =>
        reconciliationAmounts.Values.Sum() - (cashRegister?.ExpectedTotal ?? 0);

    private List<
        IGrouping<(Guid ZoneId, string ZoneName, int ZoneSortOrder), TableResponse>
    > DiningZones =>
        tables
            .GroupBy(x => (x.ZoneId, x.ZoneName, x.ZoneSortOrder))
            .OrderBy(x => x.Key.ZoneSortOrder)
            .ThenBy(x => x.Key.ZoneName)
            .ToList();
    private List<TableResponse> TablesInSelectedZone =>
        selectedZoneId is Guid zoneId ? tables.Where(x => x.ZoneId == zoneId).ToList() : [];
    private List<TableResponse> FilteredTables =>
        TablesInSelectedZone
            .Where(x => tableFilter == "All" || x.Status == tableFilter)
            .OrderBy(x => x.Code)
            .ToList();
    private List<string> Categories => menu.Select(x => x.CategoryName).Distinct().Order().ToList();
    private List<MenuItemResponse> VisibleMenu =>
        menu.Where(x => x.IsAvailable && (category is null || x.CategoryName == category))
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .ToList();
    private List<PosCartLine> Cart => cart;
    private int ItemCount => cart.Sum(x => x.Quantity);
    private decimal Total => cart.Sum(line => UnitPrice(line) * line.Quantity);
    private bool HasLocation => selectedTable is not null && session is not null;
    private bool IsTakeaway => serviceMode == "Takeaway";
    private bool hasSessionOrders;
    private string CurrentLabel =>
        IsTakeaway ? "Pedido para llevar" : selectedTable?.Label ?? "Ubicación";

    protected override async Task OnInitializedAsync()
    {
        Realtime.OrderUpdated += HandleOrderUpdated;
        DiningRealtime.TableChanged += HandleTableChanged;
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
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            login = JsonSerializer.Deserialize<LoginResponse>(json);
            if (login is null || login.ExpiresAtUtc <= DateTime.UtcNow)
            {
                await Logout();
                return;
            }
            Api.AccessToken = login.AccessToken;
            await LoadRestaurants();
            await LoadTables();
            await ConnectRealtimeAsync();
            StartReconciliation();
            StartKitchenClockRefresh();
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
            $"/api/identity/auth/start/{provider}?returnPath=%2Fpos%2F",
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
        StartReconciliation();
        StartKitchenClockRefresh();
    }

    private async Task Logout()
    {
        await Js.InvokeVoidAsync("sessionStorage.removeItem", SessionKey);
        await Realtime.DisconnectAsync();
        await DiningRealtime.DisconnectAsync();
        if (reconciliation is not null)
        {
            await reconciliation.CancelAsync();
        }

        reconciliation?.Dispose();
        reconciliation = null;
        if (kitchenClockRefresh is not null)
        {
            await kitchenClockRefresh.CancelAsync();
        }

        kitchenClockRefresh?.Dispose();
        kitchenClockRefresh = null;
        Api.AccessToken = null;
        login = null;
        restaurants = [];
        cashRegister = null;
        tables = [];
        selectedZoneId = null;
        pickupView = false;
        BackToTables();
        message = null;
    }

    private async Task RestaurantChanged(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out Guid value))
        {
            return;
        }

        restaurantId = value;
        selectedZoneId = null;
        pickupView = false;
        BackToTables();
        await LoadTables();
        await ConnectRealtimeAsync();
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
                "El usuario no tiene permisos para operar el TPV en ningún local."
            );
        }

        if (!restaurants.Any(restaurant => restaurant.Id == restaurantId))
        {
            restaurantId = restaurants[0].Id;
        }
    }

    private async Task LoadOperationalData()
    {
        SetTables(await Api.TablesAsync(restaurantId));
        cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
        PrepareReconciliation();
        await LoadPickupOrdersCore();
    }

    private async Task LoadPickupOrders()
    {
        await Run(LoadPickupOrdersCore);
    }

    private async Task ShowPickupOrders()
    {
        pickupView = true;
        selectedZoneId = null;
        tableFilter = "All";
        await LoadPickupOrders();
    }

    private void SelectZone(Guid zoneId)
    {
        selectedZoneId = zoneId;
        tableFilter = "All";
        pickupView = false;
    }

    private void SetTables(List<TableResponse> refreshedTables)
    {
        tables = refreshedTables;
        if (selectedZoneId is Guid zoneId && tables.All(x => x.ZoneId != zoneId))
        {
            selectedZoneId = null;
            tableFilter = "All";
        }
    }

    private async Task LoadPickupOrdersCore()
    {
        activeOrders = await Api.OrdersAsync(restaurantId);
        pickupOrders = activeOrders
            .Where(x => x.ServiceMode == "Takeaway")
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();
        pickupPaymentStatuses = [];
        foreach (Guid[] orderIds in pickupOrders.Select(x => x.Id).Chunk(500))
        {
            foreach (
                OrderPaymentStatusResponse status in await Api.PaymentStatusesAsync(
                    restaurantId,
                    orderIds
                )
            )
            {
                pickupPaymentStatuses[status.OrderId] = status.Status;
            }
        }
        if (
            pickupBillOpen
            && selectedPickup is not null
            && pickupOrders.FirstOrDefault(order => order.Id == selectedPickup.Id)
                is OrderResponse updated
        )
        {
            selectedPickup = updated;
            pickupPaymentStatus =
                pickupPaymentStatuses.GetValueOrDefault(updated.Id) ?? "ProjectionPending";
            await RefreshPickup();
        }
    }

    private async Task SelectTable(TableResponse table)
    {
        await Run(async () =>
        {
            if (!CashRegisterOpen && table.Status != "Occupied")
            {
                message = "Debes abrir la caja antes de ocupar una mesa.";
                return;
            }
            serviceMode = "DineIn";
            takeawayOpen = false;
            customerName = string.Empty;
            guestCountSessionOpenedHere = !table.ActiveSessionId.HasValue;
            session = table.ActiveSessionId.HasValue
                ? await Api.ActiveSessionAsync(table.Id)
                : await Api.OpenSessionAsync(table.Id);
            guestCount = session.GuestCount;
            guestCountDraft = guestCount ?? 2;
            guestCountDialogOpen = session.RequestGuestCount && session.GuestCount is null;
            guestCountTable = guestCountDialogOpen ? table : null;
            selectedTable = guestCountDialogOpen ? null : table;
            hasSessionOrders = session.Orders.Count > 0;
            menu = guestCountDialogOpen ? [] : await Api.MenuAsync(restaurantId);
            cart = [];
        });
    }

    private async Task AcceptGuestCount()
    {
        if (session is null || guestCountTable is null || guestCountDraft is < 1 or > 999 || busy)
        {
            return;
        }

        await Run(async () =>
        {
            session = await Api.SetGuestCountAsync(session.Id, guestCountDraft);
            guestCount = session.GuestCount;
            menu = await Api.MenuAsync(restaurantId);
            selectedTable = guestCountTable;
            guestCountTable = null;
            guestCountDialogOpen = false;
        });
    }

    private async Task CancelGuestCount()
    {
        if (guestCountSessionOpenedHere && session is { Orders.Count: 0 })
        {
            await Run(async () =>
            {
                await Api.CancelSessionAsync(session.Id);
                BackToTables();
                await LoadOperationalData();
            });
            return;
        }

        BackToTables();
    }

    private async Task BeginTakeaway()
    {
        await Run(async () =>
        {
            if (!CashRegisterOpen)
            {
                message = "Debes abrir la caja antes de iniciar un pedido para llevar.";
                return;
            }

            selectedTable = null;
            session = null;
            serviceMode = "Takeaway";
            takeawayOpen = true;
            pickupView = false;
            customerName = string.Empty;
            menu = await Api.MenuAsync(restaurantId);
            cart = [];
        });
    }

    private void SelectProduct(Guid productId)
    {
        MenuItemResponse? product = menu.FirstOrDefault(x => x.ProductId == productId);
        if (product is null)
            return;
        if (product.ExtraGroups is { Count: > 0 })
        {
            extraDialogProduct = product;
            editingExtraLineId = null;
            extraSelections = [];
            return;
        }
        AddCartUnit(productId, []);
    }

    private void AddCartUnit(Guid productId, List<PosCartExtra> extras)
    {
        PosCartLine? existing = cart.FirstOrDefault(line =>
            line.ProductId == productId
            && string.IsNullOrWhiteSpace(line.Notes)
            && SameExtras(line.Extras, extras)
        );
        if (existing is not null)
        {
            existing.Quantity = Math.Min(99, existing.Quantity + 1);
            return;
        }

        cart.Add(new PosCartLine { ProductId = productId, Extras = extras });
    }

    private static bool SameExtras(
        IEnumerable<PosCartExtra> left,
        IEnumerable<PosCartExtra> right
    ) =>
        left.OrderBy(extra => extra.GroupId)
            .ThenBy(extra => extra.ProductId)
            .SequenceEqual(right.OrderBy(extra => extra.GroupId).ThenBy(extra => extra.ProductId));

    private MenuItemResponse ProductFor(PosCartLine line) =>
        menu.First(x => x.ProductId == line.ProductId);

    private decimal UnitPrice(PosCartLine line)
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

    private void ChangeCartQuantity(Guid lineId, int change)
    {
        PosCartLine? line = cart.FirstOrDefault(x => x.Id == lineId);
        if (line is null)
            return;
        line.Quantity = Math.Clamp(line.Quantity + change, 0, 99);
        if (line.Quantity == 0)
            cart.Remove(line);
    }

    private void EditExtras(PosCartLine line)
    {
        extraDialogProduct = ProductFor(line);
        editingExtraLineId = line.Id;
        extraSelections = line.Extras.ToDictionary(x => (x.GroupId, x.ProductId), x => x.Quantity);
    }

    private int ExtraQuantity(ProductExtraGroup group, ProductExtraOption option) =>
        extraSelections.GetValueOrDefault((group.Id, option.ProductId));

    private int GroupExtraQuantity(ProductExtraGroup group) =>
        extraSelections.Where(x => x.Key.GroupId == group.Id).Sum(x => x.Value);

    private void ChangeExtraQuantity(ProductExtraGroup group, ProductExtraOption option, int change)
    {
        var key = (group.Id, option.ProductId);
        int next = Math.Clamp(extraSelections.GetValueOrDefault(key) + change, 0, 99);
        if (
            GroupExtraQuantity(group) + next - extraSelections.GetValueOrDefault(key)
            > group.MaxSelections
        )
            return;
        if (next == 0)
            extraSelections.Remove(key);
        else
            extraSelections[key] = next;
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
            return;
        List<PosCartExtra> selections = extraSelections
            .Where(x => x.Value > 0)
            .Select(x => new PosCartExtra(x.Key.GroupId, x.Key.ProductId, x.Value))
            .ToList();
        if (editingExtraLineId is Guid lineId)
        {
            PosCartLine? line = cart.FirstOrDefault(x => x.Id == lineId);
            if (line is not null)
            {
                line.Extras.Clear();
                line.Extras.AddRange(selections);
                MergeMatchingCartLine(line);
            }
        }
        else
        {
            AddCartUnit(extraDialogProduct.ProductId, selections);
        }
        CloseExtras();
    }

    private void MergeMatchingCartLine(PosCartLine line)
    {
        PosCartLine? matching = cart.FirstOrDefault(candidate =>
            candidate.Id != line.Id
            && candidate.ProductId == line.ProductId
            && candidate.Notes == line.Notes
            && SameExtras(candidate.Extras, line.Extras)
        );
        if (matching is null)
        {
            return;
        }

        if (matching.Quantity + line.Quantity > 99)
        {
            return;
        }

        matching.Quantity += line.Quantity;
        cart.Remove(line);
    }

    private void CloseExtras()
    {
        extraDialogProduct = null;
        editingExtraLineId = null;
        extraSelections = [];
    }

    private async Task SendOrder()
    {
        if (!CashRegisterOpen)
        {
            message = "Debes abrir la caja antes de tomar pedidos.";
            return;
        }
        if (!IsTakeaway && !HasLocation)
        {
            return;
        }

        if (IsTakeaway && string.IsNullOrWhiteSpace(customerName))
        {
            return;
        }

        await Run(
            async () =>
            {
                if (HasLocation && session is { RequestGuestCount: true, GuestCount: null })
                {
                    session = await Api.SetGuestCountAsync(session.Id, guestCount ?? 0);
                }
                OrderResponse order = IsTakeaway
                    ? await Api.CreateAndSubmitTakeawayAsync(
                        restaurantId,
                        customerName.Trim(),
                        cart
                    )
                    : await Api.CreateAndSubmitAsync(
                        restaurantId,
                        selectedTable!,
                        session!.Id,
                        serviceMode,
                        string.Empty,
                        cart
                    );

                cart.Clear();
                hasSessionOrders = HasLocation;
                success = true;
                message = IsTakeaway
                    ? $"Recogida {order.Id.ToString()[..8]} enviada a cocina para {order.CustomerName}; el cobro se registra al pagar o entregar."
                    : $"Comanda {order.Id.ToString()[..8]} enviada a cocina.";
                if (IsTakeaway)
                {
                    BackToTables();
                    await Task.Delay(400, realtimeRefresh?.Token ?? CancellationToken.None);
                    await LoadPickupOrdersCore();
                }
            },
            false
        );
    }

    private async Task OpenPickup(OrderResponse pickup)
    {
        selectedPickup = pickup;
        pickupDetail = null;
        pickupPaymentStatus =
            pickupPaymentStatuses.GetValueOrDefault(pickup.Id) ?? "ProjectionPending";
        pickupPaymentKey = Guid.Empty;
        paymentMethod = "Card";
        paymentReference = string.Empty;
        pickupBillOpen = true;
        await Run(RefreshPickup);
    }

    private async Task RefreshPickup()
    {
        if (selectedPickup is null)
        {
            return;
        }

        OrderResponse pickup = selectedPickup;
        OrderDetailResponse detail = await Api.OrderAsync(pickup.Id);
        pickupDetail = detail;
        List<OrderPaymentStatusResponse> statuses = await Api.PaymentStatusesAsync(
            restaurantId,
            pickup.Id
        );
        pickupPaymentStatus = statuses.FirstOrDefault()?.Status ?? "ProjectionPending";
        pickupPaymentStatuses[pickup.Id] = pickupPaymentStatus;
        selectedPickup = pickup with { Status = detail.Status, Total = detail.Total };
    }

    private void ClosePickup()
    {
        pickupBillOpen = false;
        selectedPickup = null;
        pickupDetail = null;
        pickupPaymentKey = Guid.Empty;
    }

    private bool CanCancelPickupLine(OrderLineDetailResponse line)
    {
        return selectedPickup is not null
            && pickupDetail is not null
            && pickupPaymentStatus != "Paid"
            && line.Status == "Active"
            && OrderProgress.CanCancelBeforePreparation(pickupDetail.Status)
            && line.KitchenStatus == "Pending";
    }

    private async Task CancelPickupLine(OrderLineDetailResponse line)
    {
        if (selectedPickup is null)
        {
            return;
        }

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

        Guid orderId = selectedPickup.Id;
        OrderResponse pickup = selectedPickup;
        await Run(
            async () =>
            {
                OrderDetailResponse detail = await Api.CancelOrderLineAsync(
                    orderId,
                    line.Id,
                    reason.Trim()
                );
                pickupDetail = detail;
                pickupPaymentStatus =
                    (await Api.PaymentStatusesAsync(restaurantId, orderId)).FirstOrDefault()?.Status
                    ?? "ProjectionPending";
                pickupPaymentStatuses[orderId] = pickupPaymentStatus;
                selectedPickup = pickup with { Status = detail.Status, Total = detail.Total };
                success = true;
                message = $"{line.ProductName} cancelado antes de preparación.";
            },
            false
        );
    }

    private async Task CapturePickup()
    {
        if (selectedPickup is null || pickupPaymentStatus != "Open" || busy)
        {
            return;
        }
        if (!CashRegisterOpen)
        {
            message = "Debes abrir la caja antes de registrar el cobro.";
            return;
        }

        Guid orderId = selectedPickup.Id;
        pickupPaymentKey = pickupPaymentKey == Guid.Empty ? Guid.NewGuid() : pickupPaymentKey;
        await Run(
            async () =>
            {
                await Api.CapturePickupAsync(
                    orderId,
                    paymentMethod,
                    cashRegister?.Id,
                    paymentReference,
                    pickupPaymentKey
                );
                pickupPaymentStatus =
                    (await Api.PaymentStatusesAsync(restaurantId, orderId)).FirstOrDefault()?.Status
                    ?? "ProjectionPending";
                pickupPaymentStatuses[orderId] = pickupPaymentStatus;
                success = true;
                message = $"Cobro registrado para {selectedPickup?.CustomerName}.";
            },
            false
        );
    }

    private async Task DeliverPickup()
    {
        if (selectedPickup is null || pickupPaymentStatus != "Paid" || busy)
        {
            return;
        }

        Guid orderId = selectedPickup.Id;
        string name = selectedPickup.CustomerName;
        await Run(
            async () =>
            {
                await Api.DeliverPickupAsync(orderId);
                success = true;
                message = $"Pedido entregado a {name}.";
                ClosePickup();
                await Task.Delay(400, realtimeRefresh?.Token ?? CancellationToken.None);
                await LoadPickupOrdersCore();
            },
            false
        );
    }

    private static string PickupPaymentLabel(string status)
    {
        return status switch
        {
            "Open" => "Pendiente",
            "Paid" => "Pagado",
            "Refunded" => "Reembolsado",
            _ => "Sin sincronizar",
        };
    }

    private async Task CancelSession()
    {
        if (session is null)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.CancelSessionAsync(session.Id);
                success = true;
                message = "Apertura cancelada y ubicación liberada.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
    }

    private async Task BackFromTableOrder()
    {
        if (HasLocation && !hasSessionOrders)
        {
            await CancelSession();
            return;
        }

        BackToTables();
    }

    private async Task OpenBill()
    {
        if (session is null || !hasSessionOrders || busy)
        {
            return;
        }

        bill = null;
        billLoadError = null;
        billOrders = [];
        currentChargeAmount = 0;
        tipPercentage = 0;
        tipAmount = 0;
        tipAmountManuallyEdited = false;
        billOpen = true;
        paymentDialogOpen = false;
        await Run(LoadBill);
    }

    private async Task LoadBill()
    {
        if (session is null)
        {
            return;
        }

        billLoadError = null;
        try
        {
            bill = await Api.BillAsync(session.Id);
            OrderDetailResponse[] details = await Task.WhenAll(
                bill.Orders.Select(order => Api.OrderAsync(order.OrderId))
            );
            billOrders = details.ToDictionary(order => order.Id);
            if (paymentDialogOpen && paymentMethod == "Transfer")
            {
                currentChargeAmount = bill.Outstanding;
            }
            SyncPaymentAmounts();
        }
        catch (Exception exception)
        {
            bill = null;
            billOrders = [];
            billLoadError = exception.Message;
            throw;
        }
    }

    private static bool CanCancelLine(
        SessionBillOrderResponse billOrder,
        OrderDetailResponse order,
        OrderLineDetailResponse line
    )
    {
        return billOrder.AmountPaid == 0
            && billOrder.PaymentStatus != "Paid"
            && line.Status == "Active"
            && OrderProgress.CanCancelBeforePreparation(order.Status)
            && line.KitchenStatus == "Pending";
    }

    private async Task CancelLine(SessionBillOrderResponse order, OrderLineDetailResponse line)
    {
        await Run(
            async () =>
            {
                await Api.CancelOrderLineAsync(order.OrderId, line.Id, "Cancelado desde TPV");
                await Task.Delay(500);
                await LoadBill();
                success = true;
                message = $"{line.ProductName} cancelado antes de preparación.";
            },
            false
        );
    }

    private void CloseBill()
    {
        paymentDialogOpen = false;
        billOpen = false;
    }

    private void OpenPaymentDialog()
    {
        if (bill is null || bill.Outstanding <= 0 || !bill.CanCheckout || busy)
        {
            return;
        }
        paymentMethod = string.Empty;
        currentChargeAmount = 0;
        tipPercentage = 0;
        tipAmount = 0;
        tipAmountManuallyEdited = false;
        paymentReference = string.Empty;
        SyncPaymentAmounts();
        paymentDialogOpen = true;
        SelectKeypadTarget("Charge");
    }

    private void ClosePaymentDialog()
    {
        if (!busy)
        {
            paymentDialogOpen = false;
        }
    }

    private void SelectKeypadTarget(string target)
    {
        if (paymentMethod == "Transfer" && target == "Charge")
        {
            return;
        }
        keypadTarget = target;
        keypadText = (target == "Tip" ? tipAmount : currentChargeAmount)
            .ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            .Replace('.', ',');
        keypadStartNew = true;
    }

    private void SetExactAmount()
    {
        if (bill is null)
        {
            return;
        }
        currentChargeAmount = bill.Outstanding;
        SyncPaymentAmounts();
        SelectKeypadTarget("Charge");
    }

    private void PressPaymentKey(string key)
    {
        if (paymentMethod == "Transfer" && keypadTarget == "Charge")
        {
            return;
        }
        string next = keypadText;
        if (key == "Borrar")
        {
            next = "0";
        }
        else if (key == ",")
        {
            next =
                keypadStartNew ? "0,"
                : next.Contains(',') ? next
                : next + ",";
        }
        else
        {
            next =
                keypadStartNew ? key
                : next == "0" ? key
                : next + key;
        }
        if (
            next.Length > 10
            || next.Contains(',') && next[(next.IndexOf(',') + 1)..].Length > 2
            || !decimal.TryParse(
                next.Replace(',', '.'),
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal amount
            )
            || amount > 1_000_000m
        )
        {
            return;
        }
        keypadStartNew = false;
        keypadText = next;
        if (keypadTarget == "Charge")
        {
            currentChargeAmount = amount;
            SyncPaymentAmounts();
        }
        else if (keypadTarget == "Tip")
        {
            tipAmount = amount;
            tipAmountManuallyEdited = true;
        }
    }

    private async Task CheckoutSession()
    {
        if (session is null || bill is null || bill.Outstanding <= 0 || !bill.CanCheckout || busy)
        {
            return;
        }

        if (!CashRegisterOpen)
        {
            message = "Debes abrir la caja antes de registrar el cobro.";
            return;
        }
        if (!IsChargeAmountValid)
        {
            return;
        }
        decimal paymentAmount = currentChargeAmount;
        decimal tipAmount = CurrentTipAmount;
        decimal amountReceived = AmountReceived;
        decimal changeAmount = ChangeAmount;
        checkoutKey = checkoutKey == Guid.Empty ? Guid.NewGuid() : checkoutKey;
        List<PosApi.CheckoutOrderAllocation> allocations = GetCurrentAllocations();
        await Run(
            async () =>
            {
                string reference = PosApi.PaymentReference(
                    paymentMethod,
                    cashRegister?.Id,
                    paymentReference
                );
                bill = await Api.CheckoutAsync(
                    session.Id,
                    checkoutKey,
                    paymentMethod,
                    reference,
                    tipAmount,
                    amountReceived,
                    allocations
                );
                checkoutKey = Guid.Empty;
                lastChangeAmount = changeAmount;
                paymentDialogOpen = false;
                currentChargeAmount = 0;
                tipAmountManuallyEdited = false;
                SyncPaymentAmounts();
                success = true;
                message =
                    $"Cobro de {paymentAmount + tipAmount:0.00} € registrado."
                    + $" Cambio a devolver: {changeAmount:0.00} €."
                    + $" Pendiente de la cuenta: {bill.Outstanding:0.00} €.";
            },
            false
        );
    }

    private List<PosApi.CheckoutOrderAllocation> GetCurrentAllocations()
    {
        if (bill is null)
        {
            return [];
        }
        decimal remainingAmount = PaymentPreview.AppliedToAccount;
        List<PosApi.CheckoutOrderAllocation> allocations = [];
        foreach (
            SessionBillOrderResponse order in bill.Orders.Where(order =>
                order.OrderStatus != "Cancelled"
            )
        )
        {
            decimal orderOutstanding = Math.Max(0, order.Amount - order.AmountPaid);
            decimal allocation = Math.Min(orderOutstanding, remainingAmount);
            if (allocation > 0)
            {
                allocations.Add(new(order.OrderId, allocation));
                remainingAmount -= allocation;
            }
            if (remainingAmount <= 0)
            {
                break;
            }
        }
        return allocations;
    }

    private void SelectTip(int percentage)
    {
        tipPercentage = percentage;
        tipAmountManuallyEdited = false;
        SyncPaymentAmounts();
        if (keypadTarget == "Tip")
        {
            SelectKeypadTarget("Tip");
        }
    }

    private void SetPaymentMethod(string method)
    {
        if (paymentMethod == "Transfer" && method != "Transfer")
        {
            currentChargeAmount = 0;
        }
        paymentMethod = method;
        if (method == "Transfer" && bill is not null)
        {
            currentChargeAmount = bill.Outstanding;
        }
        SyncPaymentAmounts();
        if (method == "Transfer")
        {
            keypadTarget = "Charge";
            keypadStartNew = true;
        }
        else
        {
            SelectKeypadTarget("Charge");
        }
    }

    private void SyncPaymentAmounts()
    {
        if (!tipAmountManuallyEdited)
        {
            tipAmount = SuggestedTipAmount;
        }
    }

    private async Task ReleaseSession()
    {
        if (session is null || bill is null || !bill.CanRelease || busy)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.ReleaseSessionAsync(session.Id);
                success = true;
                message = "Mesa liberada desde TPV.";
                BackToTables();
                await LoadOperationalData();
            },
            false
        );
    }

    private void BackToTables()
    {
        guestCountDialogOpen = false;
        guestCountSessionOpenedHere = false;
        guestCountTable = null;
        selectedTable = null;
        session = null;
        takeawayOpen = false;
        serviceMode = "DineIn";
        menu = [];
        cart = [];
        category = null;
        hasSessionOrders = false;
        bill = null;
        billOrders = [];
        billOpen = false;
        paymentDialogOpen = false;
        lastChangeAmount = null;
        checkoutKey = Guid.Empty;
        paymentReference = string.Empty;
    }

    private async Task OpenCashRegisterPanel()
    {
        cashPanelOpen = true;
        await RefreshCashRegister();
        PrepareReconciliation();
    }

    private void CloseCashRegisterPanel()
    {
        cashPanelOpen = false;
    }

    private async Task RefreshCashRegister()
    {
        await Run(
            async () =>
            {
                cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
                PrepareReconciliation();
            },
            false
        );
    }

    private async Task OpenDailyCashRegister()
    {
        await Run(
            async () =>
            {
                cashRegister = await Api.OpenCashRegisterAsync(restaurantId, openingFloat);
                PrepareReconciliation();
                success = true;
                message = $"Caja diaria abierta con {openingFloat:0.00} € de fondo.";
            },
            false
        );
    }

    private async Task CloseDailyCashRegister()
    {
        if (cashRegister is null)
        {
            return;
        }

        decimal reconciledTotal = reconciliationAmounts.Values.Sum();
        if (
            !await Js.InvokeAsync<bool>(
                "confirm",
                $"Total esperado: {cashRegister.ExpectedTotal:0.00} €. Total conciliado: {reconciledTotal:0.00} €. ¿Cerrar el turno?"
            )
        )
        {
            return;
        }

        await Run(
            async () =>
            {
                cashRegister = await Api.CloseCashRegisterAsync(
                    restaurantId,
                    cashRegister.Id,
                    reconciliationAmounts
                );
                success = true;
                message = $"Caja cerrada. Descuadre: {cashRegister.Difference:+0.00;-0.00;0.00} €.";
            },
            false
        );
    }

    private void PrepareReconciliation()
    {
        reconciliationAmounts =
            cashRegister?.ExpectedByMethod.ToDictionary(
                x => x.Method,
                x => x.Expected,
                StringComparer.OrdinalIgnoreCase
            ) ?? new(StringComparer.OrdinalIgnoreCase);
    }

    private string FilterClass(string value)
    {
        return tableFilter == value ? "filter active" : "filter";
    }

    private string ZoneClass(Guid zoneId)
    {
        return !pickupView && selectedZoneId == zoneId ? "zone-filter active" : "zone-filter";
    }

    private string CategoryClass(string? value)
    {
        return category == value ? "filter active" : "filter";
    }

    private static string StatusLabel(string status)
    {
        return status == "Occupied" ? "Ocupada" : "Disponible";
    }

    private static string KitchenLineStatusLabel(string status) =>
        status switch
        {
            "InPreparation" => "Preparando",
            "Ready" => "Esperando despacho del chef",
            "Dispatched" => "Despachado",
            _ => "Pendiente",
        };

    private static string KitchenLineStatusIcon(string status) =>
        status switch
        {
            "InPreparation" => "🍴",
            "Ready" => "⏳",
            "Dispatched" => "✔",
            _ => "⏳",
        };

    private static string KitchenLineStatusClass(string status) =>
        status switch
        {
            "InPreparation" => "preparing",
            "Ready" => "awaiting-dispatch",
            "Dispatched" => "dispatched",
            _ => "pending",
        };

    private string TableOrderSummary(TableResponse table)
    {
        if (table.ActiveSessionId is not Guid sessionId)
        {
            return "Sin comandas activas";
        }

        List<OrderResponse> orders = activeOrders
            .Where(x => x.TableId == table.Id && x.DiningSessionId == sessionId)
            .ToList();
        return OrderProgress.Summarize(orders.Select(x => x.Status));
    }

    private string? TableKitchenTime(TableResponse table)
    {
        if (table.ActiveSessionId is not Guid sessionId)
        {
            return null;
        }

        DateTime now = DateTime.UtcNow;
        TimeSpan? longestElapsed = null;
        foreach (
            OrderResponse order in activeOrders.Where(x =>
                x.TableId == table.Id && x.DiningSessionId == sessionId
            )
        )
        {
            (DateTime Start, DateTime? End)? clock = KitchenClock(order);
            if (clock is null)
            {
                continue;
            }

            TimeSpan elapsed = (clock.Value.End ?? now) - clock.Value.Start;
            if (longestElapsed is null || elapsed > longestElapsed.Value)
            {
                longestElapsed = elapsed;
            }
        }

        return longestElapsed is TimeSpan duration ? FormatKitchenDuration(duration) : null;
    }

    private static (DateTime Start, DateTime? End)? KitchenClock(OrderResponse order)
    {
        if (
            order.SubmittedAtUtc is not DateTime start
            || order.Status is "Cancelled" or "Delivered"
        )
        {
            return null;
        }

        OrderStationProgressResponse[] stations = (order.Stations ?? [])
            .Where(station => station.Status != "Cancelled")
            .ToArray();
        if (stations.Length == 0)
        {
            return null;
        }

        bool completed = stations.All(station => station.Status is "Ready" or "Dispatched");
        if (order.Status == "Ready" && stations.All(station => station.Status == "Dispatched"))
        {
            return null;
        }

        if (!completed)
        {
            return (start, null);
        }

        if (stations.Any(station => station.ReadyAtUtc is null))
        {
            return null;
        }

        DateTime end = stations.Max(station => station.ReadyAtUtc!.Value);
        return (start, end < start ? start : end);
    }

    private static string FormatKitchenDuration(TimeSpan duration)
    {
        int seconds = Math.Max(0, (int)Math.Floor(duration.TotalSeconds));
        int hours = seconds / 3600;
        int minutes = hours > 0 ? (seconds / 60) % 60 : seconds / 60;
        int remainder = seconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{remainder:00}" : $"{minutes:00}:{remainder:00}";
    }

    private string SessionOrderSummary(Guid sessionId)
    {
        return OrderProgress.Summarize(
            activeOrders.Where(x => x.DiningSessionId == sessionId).Select(x => x.Status)
        );
    }

    private static string PaymentLabel(string method)
    {
        return method switch
        {
            "Cash" => "Efectivo",
            "Card" => "Tarjeta / datáfono",
            "Transfer" => "Transferencia",
            "Online" => "Pago online",
            "Cheque" => "Cheque",
            _ => method,
        };
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
            SetTables(await Api.TablesAsync(restaurantId));
            StateHasChanged();
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

    private async Task HandleTableChanged(DiningTableChangedNotification notification)
    {
        if (login is null || notification.RestaurantId != restaurantId)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            int index = tables.FindIndex(table => table.Id == notification.TableId);
            if (index >= 0)
            {
                tables[index] = tables[index] with
                {
                    Status = notification.Status,
                    ActiveSessionId = notification.ActiveSessionId,
                };
                StateHasChanged();
            }
        });

        await ScheduleRealtimeRefresh(notification.RestaurantId);
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
                SetTables(await Api.TablesAsync(restaurantId));
                await LoadPickupOrdersCore();
                if (session is not null)
                {
                    TableResponse? current = tables.FirstOrDefault(x => x.Id == session.TableId);
                    if (current?.ActiveSessionId == session.Id)
                    {
                        if (guestCountDialogOpen)
                        {
                            guestCountTable = current;
                        }
                        else
                        {
                            selectedTable = current;
                        }
                        session = await Api.ActiveSessionAsync(current.Id);
                        hasSessionOrders = session.Orders.Count > 0;
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
                message = $"No se pudo actualizar el TPV en tiempo real: {exception.Message}";
                StateHasChanged();
            });
        }
    }

    private void StartReconciliation()
    {
        reconciliation?.Cancel();
        reconciliation?.Dispose();
        reconciliation = new CancellationTokenSource();
        _ = ReconcileOperationalDataAsync(reconciliation.Token);
    }

    private void StartKitchenClockRefresh()
    {
        kitchenClockRefresh?.Cancel();
        kitchenClockRefresh?.Dispose();
        kitchenClockRefresh = new CancellationTokenSource();
        _ = RefreshKitchenClocksAsync(kitchenClockRefresh.Token);
    }

    private async Task RefreshKitchenClocksAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (login is null || selectedTable is not null || takeawayOpen || pickupView)
                {
                    continue;
                }

                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReconcileOperationalDataAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    if (login is null || busy)
                    {
                        return;
                    }

                    SetTables(await Api.TablesAsync(restaurantId));
                    await LoadPickupOrdersCore();
                    cashRegister = await Api.CurrentCashRegisterAsync(restaurantId);
                    PrepareReconciliation();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
            {
                message =
                    $"No se pudo reconciliar el TPV con la base de datos: {exception.Message}";
                StateHasChanged();
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        Realtime.OrderUpdated -= HandleOrderUpdated;
        DiningRealtime.TableChanged -= HandleTableChanged;
        if (realtimeRefresh is not null)
        {
            await realtimeRefresh.CancelAsync();
        }

        realtimeRefresh?.Dispose();
        if (reconciliation is not null)
        {
            await reconciliation.CancelAsync();
        }

        reconciliation?.Dispose();
        if (kitchenClockRefresh is not null)
        {
            await kitchenClockRefresh.CancelAsync();
        }

        kitchenClockRefresh?.Dispose();
        await Realtime.DisconnectAsync();
        await DiningRealtime.DisconnectAsync();
    }
}
