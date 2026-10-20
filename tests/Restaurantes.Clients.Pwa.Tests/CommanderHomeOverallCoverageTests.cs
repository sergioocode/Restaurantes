using System.Reflection;
using Restaurantes.Clients.Commander.Pwa.Models;
using Restaurantes.Clients.Commander.Pwa.Pages;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class CommanderHomeOverallCoverageTests
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly string[] CategoryNames = ["Cervezas", "Pizzas", "Vinos"];
    private static readonly string[] TercioFormat = ["TERCIO"];
    private static readonly string[] TapaFormat = ["TAPA"];

    [Fact]
    public void Tax_breakdown_aggregates_rates_applies_discount_and_skips_cancelled_lines()
    {
        Home home = new();
        Guid orderId = Guid.NewGuid();
        SessionBillOrderResponse[] billOrders =
        [
            new(orderId, "Submitted", "Unpaid", 100m, 0m, DateTime.UtcNow),
            new(Guid.NewGuid(), "Cancelled", "Unpaid", 50m, 0m, DateTime.UtcNow),
        ];
        Set(
            home,
            "bill",
            new SessionBillResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Open",
                150m,
                150m,
                billOrders.ToList(),
                DiscountPercentage: 10m
            )
        );
        Set(
            home,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [orderId] = new(
                    orderId,
                    Guid.NewGuid(),
                    "Submitted",
                    100m,
                    [
                        Line(
                            "IVA 10",
                            "Active",
                            basePrice: 20m,
                            quantity: 2,
                            taxRate: 10m,
                            extras: [new(Guid.NewGuid(), "Extra", 3m, 2, 10m)]
                        ),
                        Line("IVA 21", "Active", basePrice: 12m, quantity: 1, taxRate: 21m),
                        Line("Cancelada", "Cancelled", basePrice: 200m, quantity: 1, taxRate: 10m),
                    ],
                    []
                ),
            }
        );

        object[] breakdown = GetTaxBreakdown(home);
        Assert.Equal(2, breakdown.Length);
        AssertBreakdown(breakdown[0], 10m, 42.55m, 4.25m);
        AssertBreakdown(breakdown[1], 21m, 8.93m, 1.87m);
        Assert.Equal(100m, GetProperty<decimal>(home, "BillConsumptionTotal"));
    }

    [Fact]
    public void Tax_breakdown_is_hidden_when_a_bill_order_or_line_has_no_tax_rate()
    {
        Home missingDetail = new();
        Guid orderId = Guid.NewGuid();
        Set(missingDetail, "bill", Bill(orderId));
        Set(missingDetail, "billOrders", new Dictionary<Guid, OrderDetailResponse>());
        Assert.Empty(GetTaxBreakdown(missingDetail));

        Home missingRate = new();
        Set(missingRate, "bill", Bill(orderId));
        Set(
            missingRate,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [orderId] = new(
                    orderId,
                    Guid.NewGuid(),
                    "Submitted",
                    10m,
                    [Line("Sin IVA", "Active", basePrice: 10m, quantity: 1, taxRate: null)],
                    []
                ),
            }
        );
        Assert.Empty(GetTaxBreakdown(missingRate));
    }

    [Fact]
    public void Commander_state_helpers_filter_tables_orders_and_accessible_sessions()
    {
        Home home = new();
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        TableResponse secondTable = new(
            Guid.NewGuid(),
            restaurantId,
            "B2",
            "Mesa B2",
            true,
            "Occupied",
            sessionId,
            zoneId,
            "Terraza",
            1,
            userId,
            "Comandero"
        );
        TableResponse firstTable = new(
            tableId,
            restaurantId,
            "A1",
            "Mesa A1",
            true,
            "Occupied",
            sessionId,
            zoneId,
            "Terraza",
            1,
            userId,
            "Comandero"
        );
        TableResponse inactiveTable = new(
            Guid.NewGuid(),
            restaurantId,
            "A0",
            "Mesa inactiva",
            false,
            "Available",
            null,
            zoneId,
            "Terraza",
            1
        );
        SetProperty(
            home,
            "FilteredTables",
            new List<TableResponse> { secondTable, inactiveTable, firstTable }
        );
        Set(home, "selectedZoneId", zoneId);
        Set(home, "restaurantId", restaurantId);
        Set(
            home,
            "restaurants",
            new List<RestaurantResponse> { new(restaurantId, "LOCAL", "Local", "", true) }
        );
        Set(home, "selectedTable", firstTable);
        Set(
            home,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(userId, "user", "Usuario"),
                []
            )
        );
        Set(
            home,
            "activeOrders",
            new List<OrderResponse>
            {
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    tableId,
                    sessionId,
                    "",
                    "DineIn",
                    "OnAccount",
                    "Submitted",
                    10m,
                    DateTime.UtcNow
                ),
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    tableId,
                    sessionId,
                    "",
                    "DineIn",
                    "OnAccount",
                    "Ready",
                    5m,
                    DateTime.UtcNow
                ),
            }
        );
        Set(home, "sessionId", sessionId);
        Set(
            home,
            "diningSession",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Open", userId)
        );

        TableResponse[] filtered = GetProperty<IEnumerable<TableResponse>>(
                home,
                "TablesInSelectedZone"
            )
            .ToArray();
        Assert.Equal([firstTable, secondTable], filtered);
        Assert.Equal("Terraza", GetProperty<string>(home, "SelectedZoneName"));
        Assert.Equal("Local", GetProperty<RestaurantResponse>(home, "CurrentRestaurant").Name);
        Assert.Equal("Mesa A1", GetProperty<string>(home, "CurrentLabel"));
        Assert.True(GetProperty<bool>(home, "HasSessionOrders"));
        Assert.True(Invoke<bool>(home, "IsCurrentCommanderTable", firstTable));
        Assert.True(GetProperty<bool>(home, "CanShowCancelOwnSession"));
        Assert.Equal("1 en cola · 1 lista", Invoke<string>(home, "TableOrderSummary", firstTable));
        Assert.Equal("1 en cola · 1 lista", Invoke<string>(home, "SessionOrderSummary", sessionId));

        Set(
            home,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                tableId,
                "Open",
                15m,
                5m,
                [
                    new(
                        Guid.NewGuid(),
                        "Submitted",
                        "Unpaid",
                        10m,
                        5m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                    new(
                        Guid.NewGuid(),
                        "Cancelled",
                        "Unpaid",
                        5m,
                        0m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ],
                CanRelease: true
            )
        );
        Assert.Equal(5m, GetProperty<decimal>(home, "CommanderOutstanding"));
        Assert.True(GetProperty<bool>(home, "CanChargeOwnCommanderOrders"));
        Assert.True(GetProperty<bool>(home, "CanReleasePaidSession"));

        Set(home, "selectedZoneId", null);
        Assert.Empty(GetProperty<IEnumerable<TableResponse>>(home, "TablesInSelectedZone"));
        Assert.False(Invoke<bool>(home, "IsCurrentCommanderTable", inactiveTable));
        Set(home, "login", null);
        Assert.False(GetProperty<bool>(home, "CanShowCancelOwnSession"));
    }

    [Fact]
    public void Commander_selection_and_order_line_helpers_cover_states()
    {
        Home home = new();
        Set(home, "category", "Comida");
        Set(home, "selectedFormat", "TAPA");
        Assert.Equal("category-filter active", Invoke<string>(home, "CategoryClass", "Comida"));
        Assert.Equal("category-filter", Invoke<string>(home, "CategoryClass", "Bebidas"));
        Assert.Equal("category-filter active", Invoke<string>(home, "FormatClass", "TAPA"));
        Assert.Equal("category-filter", Invoke<string>(home, "FormatClass", "PLATO"));
        Invoke(home, "SelectCategory", "Bebidas");
        Assert.Equal("Bebidas", Get(home, "category"));
        Assert.Null(Get(home, "selectedFormat"));

        SessionBillOrderResponse unpaid = new(
            Guid.NewGuid(),
            "Submitted",
            "Unpaid",
            10m,
            0m,
            DateTime.UtcNow,
            Source: "Pos"
        );
        OrderDetailResponse order = new(
            unpaid.OrderId,
            Guid.NewGuid(),
            "Submitted",
            10m,
            [Line("Pizza", "Active", 10m, 1, 10m)],
            []
        );
        OrderLineDetailResponse line = order.Lines[0];
        Assert.True(InvokeStatic<bool>("CanCancelLine", unpaid, order, line));
        Assert.True(InvokeStatic<bool>("IsPosOrder", unpaid));
        Assert.False(
            InvokeStatic<bool>("CanCancelLine", unpaid with { PaymentStatus = "Paid" }, order, line)
        );
        Assert.False(
            InvokeStatic<bool>("CanCancelLine", unpaid, order with { Status = "Ready" }, line)
        );
        Assert.False(
            InvokeStatic<bool>(
                "CanCancelLine",
                unpaid,
                order,
                line with
                {
                    KitchenStatus = "InPreparation",
                }
            )
        );
        Assert.False(
            InvokeStatic<bool>("CanCancelLine", unpaid, order, line with { Status = "Cancelled" })
        );
        Assert.Equal("Preparando", InvokeStatic<string>("KitchenLineStatusLabel", "InPreparation"));
        Assert.Equal("Esperando chef", InvokeStatic<string>("KitchenLineStatusLabel", "Ready"));
        Assert.Equal("Despachado", InvokeStatic<string>("KitchenLineStatusLabel", "Dispatched"));
        Assert.Equal("Pendiente", InvokeStatic<string>("KitchenLineStatusLabel", "Pending"));
        Assert.Equal("🍴", InvokeStatic<string>("KitchenLineStatusIcon", "InPreparation"));
        Assert.Equal("⏳", InvokeStatic<string>("KitchenLineStatusIcon", "Ready"));
        Assert.Equal("✔", InvokeStatic<string>("KitchenLineStatusIcon", "Dispatched"));
        Assert.Equal("⏳", InvokeStatic<string>("KitchenLineStatusIcon", "Pending"));
        Assert.Equal("preparing", InvokeStatic<string>("KitchenLineStatusClass", "InPreparation"));
        Assert.Equal("awaiting-dispatch", InvokeStatic<string>("KitchenLineStatusClass", "Ready"));
        Assert.Equal("dispatched", InvokeStatic<string>("KitchenLineStatusClass", "Dispatched"));
        Assert.Equal("pending", InvokeStatic<string>("KitchenLineStatusClass", "Pending"));
    }

    [Fact]
    public void Menu_filters_by_availability_category_and_format_and_chooses_drink_defaults()
    {
        Home home = new();
        MenuItemResponse beer = MenuItem(
            "CERVEZAS",
            "Cervezas",
            [new("COPA", 3m), new("TERCIO", 4m)]
        );
        MenuItemResponse wine = MenuItem("VINOS", "Vinos", [new("UNIDAD", 5m), new("COPA", 6m)]);
        MenuItemResponse food = MenuItem("PIZZAS", "Pizzas", [new("PLATO", 12m), new("TAPA", 6m)]);
        MenuItemResponse unavailable = MenuItem(
            "OCULTO",
            "Oculto",
            [new("PLATO", 1m)],
            isAvailable: false
        );
        Set(home, "menu", new List<MenuItemResponse> { beer, wine, food, unavailable });

        Assert.Equal("COPA", Invoke<string>(home, "DefaultFormatForCategory", "Cervezas"));
        Assert.Equal("UNIDAD", Invoke<string>(home, "DefaultFormatForCategory", "Vinos"));
        Assert.Equal("PLATO", Invoke<string>(home, "DefaultFormatForCategory", "Pizzas"));
        Assert.Equal(CategoryNames, Invoke<List<string>>(home, "Categories"));
        Set(home, "category", "Cervezas");
        Set(home, "selectedFormat", "TERCIO");
        Assert.Equal(TercioFormat, Invoke<List<string>>(home, "AvailableFormats"));
        Assert.Equal(4m, Invoke<decimal>(home, "DisplayPrice", beer));

        Set(home, "category", "Pizzas");
        Set(home, "selectedFormat", null);
        Assert.Equal(TapaFormat, Invoke<List<string>>(home, "AvailableFormats"));
        Assert.Same(food, Assert.Single(Invoke<List<MenuItemResponse>>(home, "VisibleMenu")));
        Set(home, "selectedFormat", "TERCIO");
        Assert.Empty(Invoke<List<MenuItemResponse>>(home, "VisibleMenu"));
        Set(
            home,
            "cart",
            new List<CommanderCartLine>
            {
                new()
                {
                    ProductId = food.ProductId,
                    Format = "PLATO",
                    BasePrice = 12m,
                },
            }
        );
        Assert.Same(food, Assert.Single(Invoke<List<MenuItemResponse>>(home, "VisibleMenu")));
    }

    [Fact]
    public void Extra_selection_enforces_group_limit_and_accepts_valid_item_into_cart()
    {
        Home home = new();
        Guid productId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        ProductExtraGroup group = new(
            groupId,
            "Extras",
            1,
            1,
            [new ProductExtraOption(extraId, "Queso", "Queso", 2m)]
        );
        MenuItemResponse product = MenuItem(
            "PIZZAS",
            "Pizzas",
            [new("PLATO", 10m)],
            productId,
            extraGroups: [group]
        );
        Set(home, "menu", new List<MenuItemResponse> { product });
        Set(home, "cart", new List<CommanderCartLine>());
        Set(home, "notes", new Dictionary<Guid, string>());
        Set(home, "extraDialogProduct", product);
        Set(home, "extraDialogFormat", "PLATO");
        Set(home, "extraDialogPrice", 10m);

        Invoke(home, "ChangeExtraQuantity", group, group.Options[0], 1);
        Assert.True(GetProperty<bool>(home, "CanAcceptExtras"));
        Invoke(home, "ChangeExtraQuantity", group, group.Options[0], 1);
        Assert.Equal(1, Invoke<int>(home, "GroupExtraQuantity", group));
        Invoke(home, "AcceptExtras");

        CommanderCartLine line = Assert.Single((List<CommanderCartLine>)Get(home, "cart")!);
        Assert.Equal(12m, line.ExpectedUnitPrice);
        Assert.Equal(1, Assert.Single(line.Extras).Quantity);
        Assert.Null(Get(home, "extraDialogProduct"));
        Assert.Equal(12m, Invoke<decimal>(home, "UnitPrice", line));
        Assert.Equal(1, Invoke<int>(home, "ProductQuantity", productId));
    }

    [Fact]
    public void Editing_extras_requires_valid_selection_and_recalculates_existing_line()
    {
        Home home = new();
        Guid productId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        Guid extraId = Guid.NewGuid();
        ProductExtraGroup group = new(
            groupId,
            "Extras",
            1,
            1,
            [new ProductExtraOption(extraId, "Queso", "Queso", 2m)]
        );
        MenuItemResponse product = MenuItem(
            "PIZZAS",
            "Pizzas",
            [new("PLATO", 10m)],
            productId,
            extraGroups: [group]
        );
        CommanderCartLine line = new()
        {
            ProductId = productId,
            Format = "PLATO",
            BasePrice = 10m,
            ExpectedUnitPrice = 12m,
            Extras = [new(groupId, extraId, 1)],
        };
        Set(home, "menu", new List<MenuItemResponse> { product });
        Set(home, "cart", new List<CommanderCartLine> { line });

        Invoke(home, "EditExtras", line);
        Assert.Equal(1, Invoke<int>(home, "ExtraQuantity", group, group.Options[0]));
        Invoke(home, "ChangeExtraQuantity", group, group.Options[0], -1);
        Assert.False(GetProperty<bool>(home, "CanAcceptExtras"));
        Invoke(home, "AcceptExtras");
        Assert.Equal(12m, line.ExpectedUnitPrice);
        Assert.Equal(1, Assert.Single(line.Extras).Quantity);

        Invoke(home, "ChangeExtraQuantity", group, group.Options[0], 1);
        Invoke(home, "AcceptExtras");

        Assert.Equal(12m, line.ExpectedUnitPrice);
        Assert.Equal(1, Assert.Single(line.Extras).Quantity);
        Assert.Null(Get(home, "extraDialogProduct"));
    }

    [Fact]
    public void Payment_keypad_handles_new_decimal_input_repeats_and_rejects_excess_precision()
    {
        Home home = new();
        Set(home, "paymentKeypadStartNew", true);
        Set(home, "paymentKeypadText", "12,34");

        Assert.True(
            InvokeTryBuildPaymentKeypadValue(home, ",", out string firstDecimal, out decimal zero)
        );
        Assert.Equal("0,", firstDecimal);
        Assert.Equal(0m, zero);

        Set(home, "paymentKeypadStartNew", false);
        Set(home, "paymentKeypadText", "12,34");
        Assert.True(
            InvokeTryBuildPaymentKeypadValue(
                home,
                ",",
                out string repeatedDecimal,
                out decimal current
            )
        );
        Assert.Equal("12,34", repeatedDecimal);
        Assert.Equal(12.34m, current);
        Assert.False(InvokeTryBuildPaymentKeypadValue(home, "5", out _, out _));
        Assert.False(InvokeTryBuildPaymentKeypadValue(home, "1000001", out _, out _));

        Set(home, "paymentKeypadText", "0");
        Invoke(home, "PressPaymentKey", "Borrar");
        Assert.Equal("0", Get(home, "paymentKeypadText"));
    }

    [Fact]
    public void Suggested_tip_respects_rounding_and_preserves_manual_amount()
    {
        Home home = new();
        Guid userId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Set(
            home,
            "bill",
            new SessionBillResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Open",
                23.45m,
                23.45m,
                [
                    new(
                        orderId,
                        "Submitted",
                        "Unpaid",
                        23.45m,
                        0m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ]
            )
        );
        Set(
            home,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(userId, "user", "Usuario"),
                []
            )
        );
        Set(home, "currentChargeAmount", 23.45m);
        Set(home, "paymentKeypadTarget", "Tip");

        Invoke(home, "SelectTip", 10);

        Assert.Equal(2.35m, GetProperty<decimal>(home, "SuggestedTipAmount"));
        Assert.Equal(2.35m, GetProperty<decimal>(home, "CurrentTipAmount"));
        Invoke(home, "PressPaymentKey", "3");
        Assert.True((bool)Get(home, "tipAmountManuallyEdited")!);
        Assert.Equal(3m, GetProperty<decimal>(home, "CurrentTipAmount"));
        Set(home, "currentChargeAmount", 10m);
        Invoke(home, "SyncPaymentAmounts");
        Assert.Equal(3m, GetProperty<decimal>(home, "CurrentTipAmount"));
        Invoke(home, "SelectTip", 15);
        Assert.Equal(1.5m, GetProperty<decimal>(home, "CurrentTipAmount"));
        Assert.Equal(11.5m, GetProperty<decimal>(home, "AmountReceived"));
    }

    [Fact]
    public void Change_quantity_adds_increments_decrements_and_removes_cart_lines()
    {
        Home home = new();
        Guid productId = Guid.NewGuid();
        MenuItemResponse product = MenuItem("PIZZAS", "Pizzas", [new("PLATO", 12m)], productId);
        Set(home, "menu", new List<MenuItemResponse> { product });
        Set(home, "cart", new List<CommanderCartLine>());
        Set(home, "notes", new Dictionary<Guid, string>());

        Invoke(home, "ChangeQuantity", productId, -1);
        Assert.Empty((List<CommanderCartLine>)Get(home, "cart")!);

        Invoke(home, "ChangeQuantity", productId, 1);
        Invoke(home, "ChangeQuantity", productId, 1);
        CommanderCartLine line = Assert.Single((List<CommanderCartLine>)Get(home, "cart")!);
        Assert.Equal(2, line.Quantity);
        Invoke(home, "ChangeQuantity", productId, -1);
        Assert.Equal(1, line.Quantity);
        Invoke(home, "ChangeQuantity", productId, -1);
        Assert.Empty((List<CommanderCartLine>)Get(home, "cart")!);

        CommanderCartLine otherFormat = new()
        {
            ProductId = productId,
            Format = "RACION",
            BasePrice = 10m,
            ExpectedUnitPrice = 10m,
        };
        Set(home, "cart", new List<CommanderCartLine> { otherFormat });
        Invoke(home, "ChangeQuantity", productId, -1);
        Assert.Empty((List<CommanderCartLine>)Get(home, "cart")!);
    }

    [Theory]
    [InlineData("Cash", "Efectivo")]
    [InlineData("Card", "Tarjeta / datáfono")]
    [InlineData("Transfer", "Transferencia")]
    [InlineData("Online", "Pago online")]
    [InlineData("Other", "Other")]
    public void Payment_method_label_maps_known_methods(string method, string expected) =>
        Assert.Equal(expected, InvokeStatic<string>("PaymentLabel", method));

    [Theory]
    [InlineData("PLATO", "Plato")]
    [InlineData("RACION", "Ración")]
    [InlineData("MEDIA_RACION", "Media ración")]
    [InlineData("TAPA", "Tapa")]
    [InlineData("UNIDAD", "Unidad")]
    [InlineData("TERCIO", "Tercio")]
    [InlineData("VASO", "Vaso")]
    [InlineData("COPA", "Copa")]
    [InlineData("COMBO", "Combo")]
    [InlineData("OTRO", "OTRO")]
    public void Format_label_maps_known_formats(string format, string expected) =>
        Assert.Equal(expected, InvokeStatic<string>("FormatLabel", format));

    private static SessionBillResponse Bill(Guid orderId) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Open",
            10m,
            10m,
            [new(orderId, "Submitted", "Unpaid", 10m, 0m, DateTime.UtcNow)]
        );

    private static OrderLineDetailResponse Line(
        string name,
        string status,
        decimal basePrice,
        int quantity,
        decimal? taxRate,
        IReadOnlyList<OrderLineExtraResponse>? extras = null
    ) =>
        new(
            Guid.NewGuid(),
            name,
            basePrice,
            quantity,
            basePrice * quantity,
            "KITCHEN",
            "Cocina",
            status,
            null,
            "",
            BaseUnitPrice: basePrice,
            Extras: extras,
            TaxRate: taxRate
        );

    private static MenuItemResponse MenuItem(
        string categoryCode,
        string categoryName,
        IReadOnlyList<MenuItemFormatPrice> formats,
        Guid? productId = null,
        bool isAvailable = true,
        IReadOnlyList<ProductExtraGroup>? extraGroups = null
    ) =>
        new(
            Guid.NewGuid(),
            productId ?? Guid.NewGuid(),
            categoryCode,
            categoryCode,
            Guid.NewGuid(),
            categoryCode,
            categoryName,
            formats[0].Price,
            isAvailable,
            "KITCHEN",
            "Cocina",
            extraGroups,
            formats[0].Format,
            formats
        );

    private static object[] GetTaxBreakdown(Home home) =>
        ((System.Collections.IEnumerable)GetProperty<object>(home, "BillTaxBreakdown"))
            .Cast<object>()
            .ToArray();

    private static void AssertBreakdown(object item, decimal rate, decimal taxableBase, decimal tax)
    {
        Type type = item.GetType();
        Assert.Equal(rate, type.GetProperty("Rate")!.GetValue(item));
        Assert.Equal(taxableBase, type.GetProperty("TaxableBase")!.GetValue(item));
        Assert.Equal(tax, type.GetProperty("TaxAmount")!.GetValue(item));
    }

    private static void Set(object instance, string name, object? value) =>
        typeof(Home).GetField(name, InstancePrivate)!.SetValue(instance, value);

    private static void SetProperty(object instance, string name, object? value) =>
        typeof(Home).GetProperty(name, InstancePrivate)!.SetValue(instance, value);

    private static object? Get(object instance, string name) =>
        typeof(Home).GetField(name, InstancePrivate)!.GetValue(instance);

    private static T GetProperty<T>(object instance, string name) =>
        (T)typeof(Home).GetProperty(name, InstancePrivate)!.GetValue(instance)!;

    private static T Invoke<T>(object instance, string name, params object?[] args) =>
        (T)typeof(Home).GetMethod(name, InstancePrivate)!.Invoke(instance, args)!;

    private static void Invoke(object instance, string name, params object?[] args) =>
        typeof(Home).GetMethod(name, InstancePrivate)!.Invoke(instance, args);

    private static T InvokeStatic<T>(string name, params object?[] args) =>
        (T)typeof(Home).GetMethod(name, StaticPrivate)!.Invoke(null, args)!;

    private static bool InvokeTryBuildPaymentKeypadValue(
        Home home,
        string key,
        out string next,
        out decimal amount
    )
    {
        object?[] args = [key, null, 0m];
        bool result = (bool)
            typeof(Home)
                .GetMethod("TryBuildPaymentKeypadValue", InstancePrivate)!
                .Invoke(home, args)!;
        next = (string)args[1]!;
        amount = (decimal)args[2]!;
        return result;
    }
}
