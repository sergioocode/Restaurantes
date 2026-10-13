using System.Text.Json;
using Microsoft.JSInterop;
using Restaurantes.Clients.Commander.Pwa.Api;
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
    private string? selectedFormat;
    private List<CommanderCartLine> cart = [];
    private Dictionary<Guid, string> notes = [];
    private MenuItemResponse? extraDialogProduct;
    private string extraDialogFormat = "PLATO";
    private decimal extraDialogPrice;
    private Guid? editingExtraLineId;
    private Dictionary<(Guid GroupId, Guid ProductId), int> extraSelections = [];
    private SessionBillResponse? bill;
    private bool billOpen;
    private bool paymentDialogOpen;
    private bool salesTicketOpen;
    private SaleResponse? salesTicket;
    private string paymentMethod = "Card";
    private string paymentReference = string.Empty;
    private string paymentKeypadTarget = "Charge";
    private string paymentKeypadText = string.Empty;
    private bool paymentKeypadStartNew = true;
    private decimal currentChargeAmount;
    private int tipPercentage;
    private bool tipAmountManuallyEdited;
    private Guid checkoutKey;
    private Dictionary<Guid, OrderDetailResponse> billOrders = [];
    private CancellationTokenSource? realtimeRefresh;

    private int ItemCount => cart.Sum(line => line.Quantity);
    private decimal Total => cart.Sum(line => UnitPrice(line) * line.Quantity);
    private decimal BillConsumptionTotal =>
        bill?.Orders.Where(order => order.OrderStatus != "Cancelled").Sum(order => order.Amount)
        ?? 0;
    private decimal CommanderOutstanding =>
        bill?.Orders.Where(order => IsOwnCommanderOrder(order) && order.OrderStatus != "Cancelled")
            .Sum(order => Math.Max(0, order.Amount - order.AmountPaid))
        ?? 0;
    private bool CanChargeOwnCommanderOrders =>
        bill is { Status: "Open" } && CommanderOutstanding > 0;
    private RestaurantResponse? CurrentRestaurant =>
        restaurants.FirstOrDefault(x => x.Id == restaurantId);
    private string CurrentLabel => selectedTable?.Label ?? string.Empty;
    private decimal CurrentTipAmount { get; set; }
    private decimal SuggestedTipAmount =>
        Math.Round(
            Math.Min(currentChargeAmount, CommanderOutstanding) * tipPercentage / 100m,
            2,
            MidpointRounding.AwayFromZero
        );
    private decimal AmountReceived => currentChargeAmount + CurrentTipAmount;
    private decimal RemainingAfterPayment =>
        Math.Max(
            0,
            CommanderOutstanding - Math.Min(Math.Max(0, currentChargeAmount), CommanderOutstanding)
        );
    private bool IsChargeAmountValid =>
        bill is not null
        && paymentMethod == "Card"
        && currentChargeAmount > 0
        && currentChargeAmount <= CommanderOutstanding
        && AmountReceived <= 1_000_000m;
    private List<string> Categories =>
        menu.Where(x => x.IsAvailable).Select(x => x.CategoryName).Distinct().Order().ToList();

    private static IReadOnlyList<MenuItemFormatPrice> PricesFor(MenuItemResponse item)
    {
        return item.Formats is { Count: > 0 }
            ? item.Formats
            : [new MenuItemFormatPrice(item.DefaultFormat, item.Price)];
    }

    private string DefaultFormatForCategory(string categoryName)
    {
        MenuItemResponse[] items = menu.Where(item =>
                item.IsAvailable && item.CategoryName == categoryName
            )
            .ToArray();
        string identity = $"{items.FirstOrDefault()?.CategoryCode} {categoryName}";
        bool isBeer = new[] { "cervez", "beer" }.Any(term =>
            identity.Contains(term, StringComparison.OrdinalIgnoreCase)
        );
        bool isDrink =
            isBeer
            || new[]
            {
                "bebid",
                "drink",
                "vino",
                "wine",
                "cóctel",
                "coctel",
                "café",
                "cafe",
                "licor",
                "refresc",
                "zumo",
                "agua",
                "infusi",
            }.Any(term => identity.Contains(term, StringComparison.OrdinalIgnoreCase));
        string[] preferred =
            isBeer ? ["COPA", "UNIDAD", "TERCIO", "VASO"]
            : isDrink ? ["UNIDAD", "COPA", "VASO", "TERCIO"]
            : ["PLATO"];
        return preferred.FirstOrDefault(format =>
                items.Any(item => PricesFor(item).Any(price => price.Format == format))
            )
            ?? items.FirstOrDefault()?.DefaultFormat
            ?? "PLATO";
    }

    private string FormatFor(MenuItemResponse item)
    {
        string format = selectedFormat ?? DefaultFormatForCategory(item.CategoryName);
        return PricesFor(item).Any(price => price.Format == format)
            ? format
            : DefaultFormatForCategory(item.CategoryName);
    }

    private List<string> AvailableFormats =>
        menu.Where(item => item.IsAvailable && (category is null || item.CategoryName == category))
            .SelectMany(PricesFor)
            .Select(price => price.Format)
            .Where(format => category is null || format != DefaultFormatForCategory(category))
            .Distinct()
            .Order()
            .ToList();
    private List<MenuItemResponse> VisibleMenu =>
        menu.Where(item =>
                item.IsAvailable
                && (category is null || item.CategoryName == category)
                && (
                    selectedFormat is null
                    || PricesFor(item).Any(price => price.Format == selectedFormat)
                    || cart.Any(line => line.ProductId == item.ProductId)
                )
            )
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .ToList();

    private decimal DisplayPrice(MenuItemResponse item)
    {
        return PricesFor(item).FirstOrDefault(price => price.Format == FormatFor(item))?.Price
            ?? item.Price;
    }

    private static string FormatLabel(string format)
    {
        return format switch
        {
            "PLATO" => "Plato",
            "RACION" => "Ración",
            "MEDIA_RACION" => "Media ración",
            "TAPA" => "Tapa",
            "UNIDAD" => "Unidad",
            "TERCIO" => "Tercio",
            "VASO" => "Vaso",
            "COPA" => "Copa",
            "COMBO" => "Combo",
            _ => format,
        };
    }

    private bool CanShowCancelOwnSession =>
        diningSession is not null && diningSession.OpenedByUserId == login?.User.Id;
    private bool CanReleasePaidSession =>
        bill is { Status: "Open", CanRelease: true }
        && bill.Orders.Any(order => IsOwnCommanderOrder(order) && order.OrderStatus != "Cancelled");

    private bool IsOwnCommanderOrder(SessionBillOrderResponse order) =>
        order.Source == "Comandero" && order.CreatedByUserId == login?.User.Id;

    private bool HasSessionOrders
    {
        get => field || activeOrders.Any(order => order.DiningSessionId == sessionId);
        set;
    }

    protected override async Task OnInitializedAsync()
    {
        Realtime.OrderUpdated += HandleOrderUpdated;
        Realtime.DiningTableUpdated += HandleDiningTableUpdated;
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
            if (guestCountSessionOpenedHere)
            {
                await Realtime.NotifyDiningTableUpdatedAsync();
            }
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
            await Realtime.NotifyDiningTableUpdatedAsync();
            guestCount = diningSession.GuestCount;
            menu = await Api.MenuAsync(restaurantId);
            selectedTable = guestCountTable;
            guestCountTable = null;
            guestCountDialogOpen = false;
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
                await Realtime.NotifyDiningTableUpdatedAsync();
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

        string format = FormatFor(product);
        decimal price = DisplayPrice(product);
        if (change > 0 && product.ExtraGroups is { Count: > 0 })
        {
            extraDialogProduct = product;
            extraDialogFormat = format;
            extraDialogPrice = price;
            editingExtraLineId = null;
            extraSelections = [];
            return;
        }

        CommanderCartLine? line = cart.LastOrDefault(item =>
            item.ProductId == productId && item.Format == format && item.BasePrice == price
        );
        if (change < 0 && line is null)
        {
            line = cart.LastOrDefault(item => item.ProductId == productId);
        }
        if (line is null)
        {
            if (change > 0)
            {
                AddCartUnit(productId, format, price, []);
            }

            return;
        }

        line.Quantity = Math.Clamp(line.Quantity + change, 0, 99);
        if (line.Quantity == 0)
        {
            cart.Remove(line);
        }
    }

    private int ProductQuantity(Guid productId)
    {
        return cart.Where(line => line.ProductId == productId).Sum(line => line.Quantity);
    }

    private MenuItemResponse ProductFor(CommanderCartLine line)
    {
        return menu.First(item => item.ProductId == line.ProductId);
    }

    private decimal UnitPrice(CommanderCartLine line)
    {
        return line.ExpectedUnitPrice;
    }

    private static decimal UnitPriceFor(
        MenuItemResponse product,
        decimal basePrice,
        IEnumerable<CommanderCartExtra> extras
    )
    {
        return basePrice
            + extras.Sum(extra =>
                (
                    product
                        .ExtraGroups?.FirstOrDefault(group => group.Id == extra.GroupId)
                        ?.Options.FirstOrDefault(option => option.ProductId == extra.ProductId)
                        ?.Price * extra.Quantity
                ) ?? 0
            );
    }

    private void AddCartUnit(
        Guid productId,
        string format,
        decimal price,
        List<CommanderCartExtra> extras
    )
    {
        MenuItemResponse product = menu.First(item => item.ProductId == productId);
        decimal expectedUnitPrice =
            price
            + extras.Sum(extra =>
                (
                    product
                        .ExtraGroups?.FirstOrDefault(group => group.Id == extra.GroupId)
                        ?.Options.FirstOrDefault(option => option.ProductId == extra.ProductId)
                        ?.Price * extra.Quantity
                ) ?? 0
            );
        CommanderCartLine? existing = cart.LastOrDefault(line =>
            line.ProductId == productId
            && line.Format == format
            && line.BasePrice == price
            && string.IsNullOrWhiteSpace(notes.GetValueOrDefault(productId))
            && SameExtras(line.Extras, extras)
        );
        if (existing is not null)
        {
            existing.Quantity = Math.Min(99, existing.Quantity + 1);
            return;
        }

        cart.Add(
            new CommanderCartLine
            {
                ProductId = productId,
                Format = format,
                BasePrice = price,
                ExpectedUnitPrice = expectedUnitPrice,
                Extras = extras,
            }
        );
    }

    private static bool SameExtras(
        IEnumerable<CommanderCartExtra> left,
        IEnumerable<CommanderCartExtra> right
    )
    {
        return left.OrderBy(extra => extra.GroupId)
            .ThenBy(extra => extra.ProductId)
            .SequenceEqual(right.OrderBy(extra => extra.GroupId).ThenBy(extra => extra.ProductId));
    }

    private void EditExtras(CommanderCartLine line)
    {
        extraDialogProduct = ProductFor(line);
        extraDialogFormat = line.Format;
        extraDialogPrice = line.BasePrice;
        editingExtraLineId = line.Id;
        extraSelections = line.Extras.ToDictionary(
            extra => (extra.GroupId, extra.ProductId),
            extra => extra.Quantity
        );
    }

    private int ExtraQuantity(ProductExtraGroup group, ProductExtraOption option)
    {
        return extraSelections.GetValueOrDefault((group.Id, option.ProductId));
    }

    private int GroupExtraQuantity(ProductExtraGroup group)
    {
        return extraSelections
            .Where(selection => selection.Key.GroupId == group.Id)
            .Sum(selection => selection.Value);
    }

    private void ChangeExtraQuantity(ProductExtraGroup group, ProductExtraOption option, int change)
    {
        (Guid Id, Guid ProductId) key = (group.Id, option.ProductId);
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
        MenuItemResponse product = extraDialogProduct;

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
                line.ExpectedUnitPrice = UnitPriceFor(product, extraDialogPrice, selections);
            }
        }
        else
        {
            AddCartUnit(product.ProductId, extraDialogFormat, extraDialogPrice, selections);
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
                {
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

    private async Task LeaveTable()
    {
        if (diningSession is null || busy)
        {
            return;
        }

        if (CanShowCancelOwnSession && !HasSessionOrders)
        {
            await Run(async () =>
            {
                await Api.CancelSessionAsync(diningSession.Id);
                await Realtime.NotifyDiningTableUpdatedAsync();
                BackToTables();
                await LoadOperationalData();
            });
            return;
        }

        BackToTables();
    }

    private async Task ReleasePaidSession()
    {
        if (diningSession is null || !CanReleasePaidSession)
        {
            return;
        }

        await Run(
            async () =>
            {
                await Api.CloseSessionAsync(diningSession.Id);
                await Realtime.NotifyDiningTableUpdatedAsync();
                success = true;
                message = "Mesa liberada.";
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

    private void OpenPaymentDialog()
    {
        if (bill is null || CommanderOutstanding <= 0 || busy)
        {
            return;
        }

        paymentReference = string.Empty;
        paymentMethod = "Card";
        currentChargeAmount = CommanderOutstanding;
        tipPercentage = 0;
        CurrentTipAmount = 0;
        tipAmountManuallyEdited = false;
        paymentDialogOpen = true;
        SelectPaymentKeypadTarget("Charge");
    }

    private void ClosePaymentDialog()
    {
        if (!busy)
        {
            paymentDialogOpen = false;
        }
    }

    private void SetPaymentMethod(string method)
    {
        if (method == "Card")
        {
            paymentMethod = method;
        }
    }

    private void SetExactPaymentAmount()
    {
        if (bill is null)
        {
            return;
        }

        currentChargeAmount = CommanderOutstanding;
        SyncPaymentAmounts();
        SelectPaymentKeypadTarget("Charge");
    }

    private void SelectPaymentKeypadTarget(string target)
    {
        paymentKeypadTarget = target;
        decimal value = target == "Tip" ? CurrentTipAmount : currentChargeAmount;
        paymentKeypadText = value
            .ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            .Replace('.', ',');
        paymentKeypadStartNew = true;
    }

    private void PressPaymentKey(string key)
    {
        string next = paymentKeypadText;
        if (key == "Borrar")
        {
            next = "0";
        }
        else
        {
            next =
                key == ","
                    ? paymentKeypadStartNew
                        ? "0,"
                        : next.Contains(',')
                            ? next
                            : next + ","
                    : paymentKeypadStartNew
                        ? key
                        : next == "0"
                            ? key
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

        paymentKeypadStartNew = false;
        paymentKeypadText = next;
        if (paymentKeypadTarget == "Charge")
        {
            currentChargeAmount = amount;
            SyncPaymentAmounts();
        }
        else
        {
            CurrentTipAmount = amount;
            tipAmountManuallyEdited = true;
        }
    }

    private void SelectTip(int percentage)
    {
        tipPercentage = percentage;
        tipAmountManuallyEdited = false;
        SyncPaymentAmounts();
        if (paymentKeypadTarget == "Tip")
        {
            SelectPaymentKeypadTarget("Tip");
        }
    }

    private void SyncPaymentAmounts()
    {
        if (!tipAmountManuallyEdited)
        {
            CurrentTipAmount = SuggestedTipAmount;
        }
    }

    private List<CommanderApi.CheckoutOrderAllocation> GetCurrentAllocations()
    {
        if (bill is null)
        {
            return [];
        }

        decimal remainingAmount = Math.Min(currentChargeAmount, CommanderOutstanding);
        List<CommanderApi.CheckoutOrderAllocation> allocations = [];
        foreach (
            SessionBillOrderResponse order in bill.Orders.Where(order =>
                IsOwnCommanderOrder(order) && order.OrderStatus != "Cancelled"
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

    private async Task CheckoutSession()
    {
        if (
            diningSession is null
            || bill is null
            || !CanChargeOwnCommanderOrders
            || !IsChargeAmountValid
            || busy
        )
        {
            return;
        }

        decimal chargedAmount = currentChargeAmount + CurrentTipAmount;
        checkoutKey = checkoutKey == Guid.Empty ? Guid.NewGuid() : checkoutKey;
        await Run(
            async () =>
            {
                bill = await Api.CheckoutAsync(
                    diningSession.Id,
                    checkoutKey,
                    paymentMethod,
                    paymentReference.Trim(),
                    CurrentTipAmount,
                    AmountReceived,
                    GetCurrentAllocations()
                );
                checkoutKey = Guid.Empty;
                paymentDialogOpen = false;
                salesTicket = null;
                await Realtime.NotifyDiningTableUpdatedAsync();
                await LoadBill();
                success = true;
                message =
                    $"Cobro con tarjeta de {chargedAmount:0.00} € registrado. Pendiente de tus comandas: {CommanderOutstanding:0.00} €.";
            },
            false
        );
    }

    private async Task OpenSalesTicket()
    {
        if (bill is null || bill.Outstanding > 0 || BillConsumptionTotal <= 0 || busy)
        {
            return;
        }

        await Run(
            async () =>
            {
                salesTicket = await Api.SaleAsync(bill.Id);
                salesTicketOpen = true;
            },
            false
        );
    }

    private void CloseSalesTicket()
    {
        salesTicketOpen = false;
        salesTicket = null;
    }

    private async Task PrintSalesTicket()
    {
        await Js.InvokeVoidAsync("window.print");
    }

    private static string PaymentLabel(string method)
    {
        return method switch
        {
            "Cash" => "Efectivo",
            "Card" => "Tarjeta / datáfono",
            "Transfer" => "Transferencia",
            "Online" => "Pago online",
            _ => method,
        };
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

    private static string KitchenLineStatusLabel(string status)
    {
        return status switch
        {
            "InPreparation" => "Preparando",
            "Ready" => "Esperando chef",
            "Dispatched" => "Despachado",
            _ => "Pendiente",
        };
    }

    private static string KitchenLineStatusIcon(string status)
    {
        return status switch
        {
            "InPreparation" => "🍴",
            "Ready" => "⏳",
            "Dispatched" => "✔",
            _ => "⏳",
        };
    }

    private static string KitchenLineStatusClass(string status)
    {
        return status switch
        {
            "InPreparation" => "preparing",
            "Ready" => "awaiting-dispatch",
            "Dispatched" => "dispatched",
            _ => "pending",
        };
    }

    private async Task CancelLine(SessionBillOrderResponse order, OrderLineDetailResponse line)
    {
        await Run(async () =>
        {
            await Api.CancelOrderLineAsync(order.OrderId, line.Id, "Cancelado desde Comandero");
            await Task.Delay(500);
            await LoadBill();
            success = true;
            message = $"{line.ProductName} cancelado antes de preparación.";
        });
    }

    private void CloseBill()
    {
        billOpen = false;
    }

    private void BackToTables()
    {
        paymentDialogOpen = false;
        salesTicketOpen = false;
        salesTicket = null;
        checkoutKey = Guid.Empty;
        guestCountDialogOpen = false;
        guestCountSessionOpenedHere = false;
        guestCountTable = null;
        selectedTable = null;
        sessionId = Guid.Empty;
        diningSession = null;
        HasSessionOrders = false;
        menu = [];
        category = null;
        selectedFormat = null;
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

    private string FormatClass(string? value)
    {
        return selectedFormat == value ? "category-filter active" : "category-filter";
    }

    private void SelectCategory(string? value)
    {
        category = value;
        selectedFormat = null;
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

    private bool IsCurrentCommanderTable(TableResponse table)
    {
        return table.ActiveSessionSource == "Comandero"
            && table.ActiveSessionOpenedByUserId == login?.User.Id;
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

    private Task HandleDiningTableUpdated(Guid changedRestaurantId)
    {
        return ScheduleRealtimeRefresh(changedRestaurantId);
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
        Realtime.DiningTableUpdated -= HandleDiningTableUpdated;
        if (realtimeRefresh is not null)
        {
            await realtimeRefresh.CancelAsync();
        }

        realtimeRefresh?.Dispose();
        await Realtime.DisconnectAsync();
    }
}
