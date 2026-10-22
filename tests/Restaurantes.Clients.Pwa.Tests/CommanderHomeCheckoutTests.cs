using System.Net;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Restaurantes.Clients.Commander.Pwa.Api;
using Restaurantes.Clients.Commander.Pwa.Models;
using Restaurantes.Clients.Commander.Pwa.Pages;
using Restaurantes.Clients.Commander.Pwa.Realtime;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class CommanderHomeCheckoutTests : BunitContext
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Tax_breakdown_applies_the_discount_before_extracting_each_rate()
    {
        SortedDictionary<decimal, decimal> grossByRate = new() { [10m] = 110m, [21m] = 121m };
        MethodInfo build = typeof(Home).GetMethod(
            "BuildTaxBreakdown",
            BindingFlags.Static | PrivateInstance
        )!;

        object[] lines = (
            (System.Collections.IEnumerable)build.Invoke(null, [grossByRate, false, 10m])!
        )
            .Cast<object>()
            .ToArray();

        Assert.Equal(2, lines.Length);
        Assert.Equal(10m, lines[0].GetType().GetProperty("Rate")!.GetValue(lines[0]));
        Assert.Equal(90m, lines[0].GetType().GetProperty("TaxableBase")!.GetValue(lines[0]));
        Assert.Equal(9m, lines[0].GetType().GetProperty("TaxAmount")!.GetValue(lines[0]));
        Assert.Equal(21m, lines[1].GetType().GetProperty("Rate")!.GetValue(lines[1]));
        Assert.Equal(90m, lines[1].GetType().GetProperty("TaxableBase")!.GetValue(lines[1]));
        Assert.Equal(18.9m, lines[1].GetType().GetProperty("TaxAmount")!.GetValue(lines[1]));
        Assert.Empty((System.Collections.IEnumerable)build.Invoke(null, [grossByRate, true, 10m])!);
        Assert.Empty(
            (System.Collections.IEnumerable)
                build.Invoke(null, [new SortedDictionary<decimal, decimal>(), false, 0m])!
        );
    }

    [Fact]
    public void Menu_format_defaults_follow_category_and_only_offer_non_default_formats()
    {
        Home page = new();
        MenuItemResponse beer = MenuItem(
            "Cerveza",
            "CERVEZAS",
            [new("TERCIO", 4m), new("COPA", 3m)]
        );
        MenuItemResponse drink = MenuItem(
            "Bebidas",
            "BEBIDAS",
            [new("COPA", 5m), new("UNIDAD", 2m)]
        );
        MenuItemResponse unavailable = MenuItem(
            "Bebidas",
            "BEBIDAS",
            [new("TERCIO", 4m)],
            isAvailable: false
        );
        MenuItemResponse food = MenuItem("Platos", "COMIDA", [new("TAPA", 6m)]);
        Set(page, "menu", new List<MenuItemResponse> { beer, drink, unavailable, food });

        MethodInfo defaultFormat = typeof(Home).GetMethod(
            "DefaultFormatForCategory",
            PrivateInstance
        )!;
        MethodInfo availableFormats = typeof(Home).GetMethod("AvailableFormats", PrivateInstance)!;
        MethodInfo formatFor = typeof(Home).GetMethod("FormatFor", PrivateInstance)!;

        Assert.Equal("COPA", defaultFormat.Invoke(page, ["Cerveza"]));
        Assert.Equal("UNIDAD", defaultFormat.Invoke(page, ["Bebidas"]));
        Assert.Equal("TAPA", defaultFormat.Invoke(page, ["Platos"]));
        Assert.Equal("PLATO", defaultFormat.Invoke(page, ["Sin productos"]));

        Set(page, "category", "Bebidas");
        Assert.Equal(
            "COPA",
            Assert.Single((IEnumerable<string>)availableFormats.Invoke(page, null)!)
        );
        Set(page, "selectedFormat", "TERCIO");
        Assert.Equal("UNIDAD", formatFor.Invoke(page, [drink]));
    }

    [Fact]
    public void Changing_menu_quantity_adds_increments_decrements_and_removes_cart_line()
    {
        Home page = new();
        MenuItemResponse product = MenuItem("Platos", "COMIDA", [new("PLATO", 7m)]);
        Set(page, "menu", new List<MenuItemResponse> { product });
        MethodInfo changeQuantity = typeof(Home).GetMethod("ChangeQuantity", PrivateInstance)!;

        changeQuantity.Invoke(page, [product.ProductId, 1]);
        changeQuantity.Invoke(page, [product.ProductId, 1]);
        List<CommanderCartLine> cart = (List<CommanderCartLine>)Get(page, "cart")!;
        CommanderCartLine line = Assert.Single(cart);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(7m, line.ExpectedUnitPrice);

        changeQuantity.Invoke(page, [product.ProductId, -1]);
        Assert.Equal(1, line.Quantity);
        changeQuantity.Invoke(page, [product.ProductId, -1]);

        Assert.Empty(cart);
        changeQuantity.Invoke(page, [Guid.NewGuid(), 1]);
        Assert.Empty(cart);
    }

    [Fact]
    public void Extra_selection_enforces_group_limits_prices_and_merges_equivalent_lines()
    {
        Home page = new();
        Guid groupId = Guid.NewGuid();
        Guid cheeseId = Guid.NewGuid();
        Guid baconId = Guid.NewGuid();
        ProductExtraGroup group = new(
            groupId,
            "Añadidos",
            1,
            2,
            [new(cheeseId, "CHEESE", "Queso", 1.5m), new(baconId, "BACON", "Bacon", 2m)]
        );
        MenuItemResponse product = MenuItem("Comida", "FOOD", [new("PLATO", 8m)]) with
        {
            ExtraGroups = [group],
        };
        Set(page, "menu", new List<MenuItemResponse> { product });
        MethodInfo changeQuantity = typeof(Home).GetMethod("ChangeQuantity", PrivateInstance)!;
        MethodInfo changeExtraQuantity = typeof(Home).GetMethod(
            "ChangeExtraQuantity",
            PrivateInstance
        )!;
        MethodInfo acceptExtras = typeof(Home).GetMethod("AcceptExtras", PrivateInstance)!;
        PropertyInfo canAcceptExtras = typeof(Home).GetProperty(
            "CanAcceptExtras",
            PrivateInstance
        )!;
        ProductExtraOption cheese = group.Options[0];
        ProductExtraOption bacon = group.Options[1];

        changeQuantity.Invoke(page, [product.ProductId, 1]);
        Assert.False((bool)canAcceptExtras.GetValue(page)!);
        changeExtraQuantity.Invoke(page, [group, cheese, 1]);
        changeExtraQuantity.Invoke(page, [group, bacon, 1]);
        Assert.True((bool)canAcceptExtras.GetValue(page)!);
        changeExtraQuantity.Invoke(page, [group, cheese, 1]);
        acceptExtras.Invoke(page, null);

        List<CommanderCartLine> cart = (List<CommanderCartLine>)Get(page, "cart")!;
        CommanderCartLine line = Assert.Single(cart);
        Assert.Equal(11.5m, line.ExpectedUnitPrice);
        Assert.Equal(2, line.Extras.Sum(extra => extra.Quantity));

        changeQuantity.Invoke(page, [product.ProductId, 1]);
        changeExtraQuantity.Invoke(page, [group, bacon, 1]);
        changeExtraQuantity.Invoke(page, [group, cheese, 1]);
        acceptExtras.Invoke(page, null);
        Assert.Same(line, Assert.Single(cart));
        Assert.Equal(2, line.Quantity);

        typeof(Home).GetMethod("EditExtras", PrivateInstance)!.Invoke(page, [line]);
        changeExtraQuantity.Invoke(page, [group, cheese, -1]);
        acceptExtras.Invoke(page, null);
        Assert.Equal(10m, line.ExpectedUnitPrice);
        Assert.Equal(new CommanderCartExtra(groupId, baconId, 1), Assert.Single(line.Extras));
    }

    [Fact]
    public async Task Commander_renders_and_edits_selected_cart_extras()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        Guid cheeseId = Guid.NewGuid();
        Guid baconId = Guid.NewGuid();
        ProductExtraGroup group = new(
            groupId,
            "Añadidos",
            1,
            1,
            [new(cheeseId, "CHEESE", "Queso", 1.5m), new(baconId, "BACON", "Bacon", 2m)]
        );
        MenuItemResponse product = MenuItem("Comida", "FOOD", [new("PLATO", 8m)]) with
        {
            ExtraGroups = [group],
        };
        HttpClient http = new(
            new CheckoutHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        Set(cut.Instance, "initializing", false);
        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "user", "Usuario"),
                []
            )
        );
        Set(cut.Instance, "restaurantId", restaurantId);
        Set(
            cut.Instance,
            "selectedTable",
            new TableResponse(
                tableId,
                restaurantId,
                "M1",
                "Mesa 1",
                true,
                "Occupied",
                sessionId,
                Guid.NewGuid(),
                "Sala",
                1
            )
        );
        Set(cut.Instance, "sessionId", sessionId);
        Set(cut.Instance, "menu", new List<MenuItemResponse> { product });
        Set(cut.Instance, "notes", new Dictionary<Guid, string> { [product.ProductId] = "" });
        cut.Render();

        cut.Find(".product-card .stepper button:last-child").Click();
        Assert.Contains("EXTRAS", cut.Markup);
        Assert.Contains("Elige 1–1", cut.Markup);
        cut.Find(".extra-option .stepper button:last-child").Click();
        cut.Find(".extras-card button.primary").Click();

        CommanderCartLine line = Assert.Single((List<CommanderCartLine>)Get(cut.Instance, "cart")!);
        Assert.Equal(9.5m, line.ExpectedUnitPrice);
        Assert.Contains("1 × Queso", cut.Markup);
        Assert.Contains("Editar extras", cut.Markup);

        cut.Find(".edit-extras").Click();
        cut.FindAll(".extra-option .stepper button")[0].Click();
        cut.FindAll(".extra-option .stepper button")[3].Click();
        cut.Find(".extras-card button.primary").Click();

        Assert.Equal(10m, line.ExpectedUnitPrice);
        Assert.Contains("1 × Bacon", cut.Markup);
        Assert.DoesNotContain("1 × Queso", cut.Markup);
    }

    [Fact]
    public void Exact_payment_amount_uses_outstanding_own_orders_and_updates_tip_keypad()
    {
        Home page = new();
        MethodInfo setExactAmount = typeof(Home).GetMethod(
            "SetExactPaymentAmount",
            PrivateInstance
        )!;
        Set(page, "currentChargeAmount", 3m);
        Set(page, "paymentKeypadTarget", "Tip");

        setExactAmount.Invoke(page, null);

        Assert.Equal(3m, Get(page, "currentChargeAmount"));
        Assert.Equal("Tip", Get(page, "paymentKeypadTarget"));

        Guid userId = Guid.NewGuid();
        Set(
            page,
            "login",
            new LoginResponse(
                "active-token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(userId, "user", "Usuario"),
                []
            )
        );
        Set(
            page,
            "bill",
            new SessionBillResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Open",
                30m,
                15m,
                [
                    new(
                        Guid.NewGuid(),
                        "Submitted",
                        "Unpaid",
                        20m,
                        5m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ]
            )
        );
        Set(page, "tipPercentage", 10);

        setExactAmount.Invoke(page, null);

        Assert.Equal(15m, Get(page, "currentChargeAmount"));
        Assert.Equal(
            1.5m,
            typeof(Home).GetProperty("CurrentTipAmount", PrivateInstance)!.GetValue(page)
        );
        Assert.Equal("Charge", Get(page, "paymentKeypadTarget"));
        Assert.Equal("15,00", Get(page, "paymentKeypadText"));
        Assert.True((bool)Get(page, "paymentKeypadStartNew")!);
    }

    [Fact]
    public async Task Accepting_login_loads_restaurant_operations_and_reports_realtime_failure()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        List<string> paths = [];
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                paths.Add(path);
                if (path == "/api/identity/auth/provider")
                    return Task.FromResult(Json(new ProviderSettingsResponse("", false, false)));
                if (path == "/api/restaurant-operations/restaurants/accessible/commander")
                    return Task.FromResult(
                        Json(
                            new[]
                            {
                                new RestaurantResponse(
                                    restaurantId,
                                    "CENTRO",
                                    "Restaurante Centro",
                                    "Calle Mayor",
                                    true
                                ),
                            }
                        )
                    );
                if (path == $"/api/dining/restaurants/{restaurantId}/tables")
                    return Task.FromResult(
                        Json(
                            new[]
                            {
                                new TableResponse(
                                    tableId,
                                    restaurantId,
                                    "M1",
                                    "Mesa 1",
                                    true,
                                    "Available",
                                    null,
                                    zoneId,
                                    "Terraza",
                                    1
                                ),
                            }
                        )
                    );
                if (path == "/api/orders/commander")
                    return Task.FromResult(
                        Json(
                            new[]
                            {
                                new OrderResponse(
                                    orderId,
                                    restaurantId,
                                    tableId,
                                    null,
                                    "",
                                    "DineIn",
                                    "OnAccount",
                                    "Submitted",
                                    12m,
                                    DateTime.UtcNow
                                ),
                            }
                        )
                    );
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();

        await cut.InvokeAsync(() =>
            (Task)
                typeof(Home)
                    .GetMethod("AcceptLogin", PrivateInstance)!
                    .Invoke(
                        cut.Instance,
                        [
                            new LoginResponse(
                                "session-token",
                                DateTime.UtcNow.AddHours(1),
                                new LoginUser(Guid.NewGuid(), "sergio", "Sergio"),
                                []
                            ),
                        ]
                    )!
        );

        Assert.Equal("session-token", api.AccessToken);
        Assert.Equal(restaurantId, Get(cut.Instance, "restaurantId"));
        Assert.Equal(
            tableId,
            Assert
                .Single(
                    (List<TableResponse>)
                        typeof(Home)
                            .GetProperty("FilteredTables", PrivateInstance)!
                            .GetValue(cut.Instance)!
                )
                .Id
        );
        Assert.Equal(
            orderId,
            Assert.Single((List<OrderResponse>)Get(cut.Instance, "activeOrders")!).Id
        );
        Assert.Contains(
            "Datos cargados, pero sin actualización en tiempo real",
            (string)Get(cut.Instance, "message")!
        );
        Assert.Contains($"/api/dining/restaurants/{restaurantId}/tables", paths);
        Assert.Contains($"/api/orders/commander", paths);
    }

    [Fact]
    public async Task Starting_login_navigates_to_the_selected_identity_provider()
    {
        HttpClient http = new(
            new CheckoutHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();

        typeof(Home).GetMethod("StartLogin", PrivateInstance)!.Invoke(cut.Instance, ["microsoft"]);

        Assert.EndsWith(
            "/api/identity/auth/start/microsoft?returnPath=%2Fcommander%2F",
            Services.GetRequiredService<NavigationManager>().Uri
        );
    }

    [Fact]
    public async Task Expired_saved_session_is_removed_before_showing_the_login_screen()
    {
        HttpClient http = new(
            new CheckoutHandler(request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                        : new HttpResponseMessage(HttpStatusCode.NotFound)
                )
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        LoginResponse expired = new(
            "expired-token",
            DateTime.UtcNow.AddMinutes(-1),
            new LoginUser(Guid.NewGuid(), "user", "Usuario"),
            []
        );
        SessionStorageJsRuntime js = new(JsonSerializer.Serialize(expired, JsonOptions));
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime>(js);

        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);

        Assert.Null(Get(cut.Instance, "login"));
        Assert.True(js.RemovedSession);
        Assert.Contains("Comandero", cut.Markup);
        Assert.Contains("Iniciar sesión con Microsoft", cut.Markup);
    }

    [Fact]
    public async Task Login_error_fragment_is_cleared_and_shows_the_authorization_message()
    {
        HttpClient http = new(
            new CheckoutHandler(request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                        : new HttpResponseMessage(HttpStatusCode.NotFound)
                )
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.BaseUri + "#other=value&login_error=Access%20Denied");

        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);

        Assert.Contains("La cuenta no está autorizada", cut.Markup);
    }

    [Fact]
    public async Task Login_code_is_exchanged_and_denied_when_no_commander_restaurant_is_available()
    {
        List<string> requests = [];
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add(request.Method.Method + " " + path);
                return Task.FromResult(
                    path switch
                    {
                        "/api/identity/auth/provider" => Json(
                            new ProviderSettingsResponse("Microsoft", true, false)
                        ),
                        "/api/identity/auth/exchange" => Json(
                            new LoginResponse(
                                "commander-token",
                                DateTime.UtcNow.AddHours(1),
                                new LoginUser(Guid.NewGuid(), "user", "Usuario"),
                                []
                            )
                        ),
                        "/api/restaurant-operations/restaurants/accessible/commander" => Json(
                            new List<RestaurantResponse>()
                        ),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
                );
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.BaseUri + "#login_code=one-time-code");

        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);

        Assert.Contains("POST /api/identity/auth/exchange", requests);
        Assert.Contains(
            "GET /api/restaurant-operations/restaurants/accessible/commander",
            requests
        );
        Assert.Equal("commander-token", api.AccessToken);
        Assert.Contains("no tiene permisos para operar el Comandero", cut.Markup);
    }

    [Fact]
    public async Task Changing_restaurant_clears_stale_selection_and_loads_active_tables()
    {
        Guid currentRestaurantId = Guid.NewGuid();
        Guid nextRestaurantId = Guid.NewGuid();
        Guid previousZoneId = Guid.NewGuid();
        Guid nextZoneId = Guid.NewGuid();
        TableResponse activeTable = new(
            Guid.NewGuid(),
            nextRestaurantId,
            "M02",
            "Mesa 2",
            true,
            "Available",
            null,
            nextZoneId,
            "Terraza",
            2
        );
        TableResponse inactiveTable = activeTable with { Id = Guid.NewGuid(), IsActive = false };
        List<string> requests = [];
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add(path);
                return Task.FromResult(
                    path == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                    : path == $"/api/dining/restaurants/{nextRestaurantId}/tables"
                        ? Json(new[] { inactiveTable, activeTable })
                    : path == $"/api/orders/commander?restaurantId={nextRestaurantId}"
                        ? Json(Array.Empty<OrderResponse>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                );
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);
        Set(cut.Instance, "restaurantId", currentRestaurantId);
        Set(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new(currentRestaurantId, "R1", "Uno", "Calle 1", true),
                new(nextRestaurantId, "R2", "Dos", "Calle 2", true),
            }
        );
        Set(cut.Instance, "selectedZoneId", previousZoneId);
        Set(cut.Instance, "selectedTable", activeTable);
        MethodInfo change = typeof(Home).GetMethod("RestaurantChanged", PrivateInstance)!;

        await cut.InvokeAsync(() =>
            (Task)change.Invoke(cut.Instance, [new ChangeEventArgs { Value = "bad-id" }])!
        );
        await cut.InvokeAsync(() =>
            (Task)
                change.Invoke(
                    cut.Instance,
                    [new ChangeEventArgs { Value = currentRestaurantId.ToString() }]
                )!
        );
        Assert.DoesNotContain(requests, path => path.Contains("/tables", StringComparison.Ordinal));

        await cut.InvokeAsync(() =>
            (Task)
                change.Invoke(
                    cut.Instance,
                    [new ChangeEventArgs { Value = nextRestaurantId.ToString() }]
                )!
        );

        Assert.Equal(nextRestaurantId, Get(cut.Instance, "restaurantId"));
        Assert.Null(Get(cut.Instance, "selectedZoneId"));
        Assert.Null(Get(cut.Instance, "selectedTable"));
        Assert.Equal(
            activeTable.Id,
            Assert
                .Single(
                    (List<TableResponse>)
                        typeof(Home)
                            .GetProperty("FilteredTables", PrivateInstance)!
                            .GetValue(cut.Instance)!
                )
                .Id
        );
        Assert.Contains($"/api/orders/commander?restaurantId={nextRestaurantId}", requests);
        Assert.False((bool)Get(cut.Instance, "busy")!);

        Set(cut.Instance, "selectedZoneId", previousZoneId);
        MethodInfo reload = typeof(Home).GetMethod("LoadOperationalData", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)reload.Invoke(cut.Instance, null)!);
        Assert.Null(Get(cut.Instance, "selectedZoneId"));
    }

    [Fact]
    public async Task Leaving_an_empty_session_opened_by_the_current_user_cancels_it()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        List<string> requests = [];
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add($"{request.Method} {path}");
                return Task.FromResult(
                    path == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                    : path == $"/api/dining/sessions/{sessionId}/cancel"
                        ? Json(
                            new DiningSessionResponse(
                                sessionId,
                                restaurantId,
                                tableId,
                                "Cancelled",
                                userId
                            )
                        )
                    : path == $"/api/dining/restaurants/{restaurantId}/tables"
                        ? Json(Array.Empty<TableResponse>())
                    : path == $"/api/orders/commander?restaurantId={restaurantId}"
                        ? Json(Array.Empty<OrderResponse>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                );
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);
        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(userId, "user", "Usuario"),
                []
            )
        );
        Set(cut.Instance, "restaurantId", restaurantId);
        Set(cut.Instance, "sessionId", sessionId);
        Set(
            cut.Instance,
            "diningSession",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Open", userId)
        );
        MethodInfo leave = typeof(Home).GetMethod("LeaveTable", PrivateInstance)!;

        await cut.InvokeAsync(() => (Task)leave.Invoke(cut.Instance, null)!);

        Assert.Contains($"POST /api/dining/sessions/{sessionId}/cancel", requests);
        Assert.Null(Get(cut.Instance, "diningSession"));
        Assert.Equal(Guid.Empty, Get(cut.Instance, "sessionId"));
        Assert.False((bool)Get(cut.Instance, "busy")!);
    }

    [Fact]
    public async Task Realtime_order_event_refreshes_tables_orders_and_open_bill()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        TableResponse table = new(
            tableId,
            restaurantId,
            "M01",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            Guid.NewGuid(),
            "Sala",
            1
        );
        OrderResponse activeOrder = new(
            orderId,
            restaurantId,
            tableId,
            sessionId,
            "Mesa 1",
            "DineIn",
            "OnAccount",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        SessionBillResponse bill = new(
            sessionId,
            restaurantId,
            tableId,
            "Open",
            12m,
            12m,
            [new(orderId, "Submitted", "Unpaid", 12m, 0m, DateTime.UtcNow, Source: "Comandero")]
        );
        List<string> requests = [];
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add(path);
                return Task.FromResult(
                    path == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                    : path == $"/api/dining/restaurants/{restaurantId}/tables"
                        ? Json(new[] { table })
                    : path == $"/api/orders/commander?restaurantId={restaurantId}"
                        ? Json(new[] { activeOrder })
                    : path == $"/api/dining/sessions/{sessionId}/bill" ? Json(bill)
                    : path == $"/api/orders/{orderId}"
                        ? Json(
                            new OrderDetailResponse(orderId, restaurantId, "Submitted", 12m, [], [])
                        )
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                );
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);
        Set(cut.Instance, "restaurantId", restaurantId);
        Set(cut.Instance, "sessionId", sessionId);
        Set(cut.Instance, "selectedTable", table with { Status = "Available" });
        Set(cut.Instance, "billOpen", true);
        MethodInfo refresh = typeof(Home).GetMethod(
            "RefreshAfterOrderEventAsync",
            PrivateInstance
        )!;

        await cut.InvokeAsync(() => (Task)refresh.Invoke(cut.Instance, [CancellationToken.None])!);

        Assert.Equal(table, Get(cut.Instance, "selectedTable"));
        Assert.Equal(
            activeOrder.Id,
            Assert.Single((List<OrderResponse>)Get(cut.Instance, "activeOrders")!).Id
        );
        SessionBillResponse refreshedBill = (SessionBillResponse)Get(cut.Instance, "bill")!;
        Assert.Equal(bill.Id, refreshedBill.Id);
        Assert.Equal(orderId, Assert.Single(refreshedBill.Orders).OrderId);
        Assert.Contains($"/api/dining/restaurants/{restaurantId}/tables", requests);
        Assert.Contains($"/api/orders/commander?restaurantId={restaurantId}", requests);
        Assert.Contains($"/api/dining/sessions/{sessionId}/bill", requests);
    }

    [Fact]
    public async Task Realtime_refresh_ignores_cancellation_and_reports_server_failures()
    {
        Guid restaurantId = Guid.NewGuid();
        HttpClient http = new(
            new CheckoutHandler(request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                    : request.RequestUri.AbsolutePath
                    == $"/api/dining/restaurants/{restaurantId}/tables"
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                )
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);
        Set(cut.Instance, "restaurantId", restaurantId);
        MethodInfo refresh = typeof(Home).GetMethod(
            "RefreshAfterOrderEventAsync",
            PrivateInstance
        )!;
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await cut.InvokeAsync(() => (Task)refresh.Invoke(cut.Instance, [cancelled.Token])!);

        Assert.Null(Get(cut.Instance, "message"));
        await cut.InvokeAsync(() => (Task)refresh.Invoke(cut.Instance, [CancellationToken.None])!);

        Assert.Contains(
            "No se pudo actualizar el comandero en tiempo real",
            (string)Get(cut.Instance, "message")!
        );
    }

    [Fact]
    public async Task Realtime_notifications_only_schedule_for_the_current_authenticated_restaurant()
    {
        Guid restaurantId = Guid.NewGuid();
        HttpClient http = new(
            new CheckoutHandler(request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                        : new HttpResponseMessage(HttpStatusCode.NotFound)
                )
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);
        Set(cut.Instance, "restaurantId", restaurantId);
        MethodInfo schedule = typeof(Home).GetMethod("ScheduleRealtimeRefresh", PrivateInstance)!;

        await cut.InvokeAsync(() => (Task)schedule.Invoke(cut.Instance, [restaurantId])!);
        Assert.Null(Get(cut.Instance, "realtimeRefresh"));

        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "active-token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "user", "Usuario"),
                []
            )
        );
        await cut.InvokeAsync(() => (Task)schedule.Invoke(cut.Instance, [Guid.NewGuid()])!);
        Assert.Null(Get(cut.Instance, "realtimeRefresh"));

        await cut.InvokeAsync(() => (Task)schedule.Invoke(cut.Instance, [restaurantId])!);
        CancellationTokenSource firstRefresh = Assert.IsType<CancellationTokenSource>(
            Get(cut.Instance, "realtimeRefresh")
        );
        await cut.InvokeAsync(() => (Task)schedule.Invoke(cut.Instance, [restaurantId])!);
        CancellationTokenSource secondRefresh = Assert.IsType<CancellationTokenSource>(
            Get(cut.Instance, "realtimeRefresh")
        );

        Assert.True(firstRefresh.IsCancellationRequested);
        Assert.NotSame(firstRefresh, secondRefresh);
        Assert.False(secondRefresh.IsCancellationRequested);
    }

    [Fact]
    public async Task Opening_a_bill_loads_its_order_details()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        SessionBillResponse bill = new(
            sessionId,
            restaurantId,
            tableId,
            "Open",
            18m,
            18m,
            [new(orderId, "Submitted", "Unpaid", 18m, 0m, DateTime.UtcNow, Source: "Comandero")]
        );
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                return Task.FromResult(
                    path == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                    : path == $"/api/dining/sessions/{sessionId}/bill" ? Json(bill)
                    : path == $"/api/orders/{orderId}"
                        ? Json(
                            new OrderDetailResponse(orderId, restaurantId, "Submitted", 18m, [], [])
                        )
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                );
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);
        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "active-token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "user", "Usuario"),
                []
            )
        );
        Set(cut.Instance, "sessionId", sessionId);
        MethodInfo openBill = typeof(Home).GetMethod("OpenBill", PrivateInstance)!;

        await cut.InvokeAsync(() => (Task)openBill.Invoke(cut.Instance, null)!);

        Assert.True((bool)Get(cut.Instance, "billOpen")!);
        Assert.Equal(bill.Id, ((SessionBillResponse)Get(cut.Instance, "bill")!).Id);
        Assert.True(
            ((Dictionary<Guid, OrderDetailResponse>)Get(cut.Instance, "billOrders")!).ContainsKey(
                orderId
            )
        );
        Assert.Null(Get(cut.Instance, "message"));
        Assert.False((bool)Get(cut.Instance, "busy")!);
    }

    [Fact]
    public async Task Logout_clears_the_session_and_operational_state()
    {
        HttpClient http = new(
            new CheckoutHandler(request =>
                Task.FromResult(
                    request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                        ? Json(new ProviderSettingsResponse("", false, false))
                        : new HttpResponseMessage(HttpStatusCode.NotFound)
                )
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http) { AccessToken = "active-token" };
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        cut.WaitForState(() => !(bool)Get(cut.Instance, "initializing")!);
        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "active-token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "user", "Usuario"),
                []
            )
        );
        Set(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse> { new(Guid.NewGuid(), "R1", "Restaurante", "Calle", true) }
        );
        Set(
            cut.Instance,
            "FilteredTables",
            new List<TableResponse>
            {
                new(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "M01",
                    "Mesa 1",
                    true,
                    "Available",
                    null,
                    Guid.NewGuid(),
                    "Sala",
                    1
                ),
            }
        );

        MethodInfo logout = typeof(Home).GetMethod("Logout", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)logout.Invoke(cut.Instance, null)!);

        Assert.Null(api.AccessToken);
        Assert.Null(Get(cut.Instance, "login"));
        Assert.Empty((List<RestaurantResponse>)Get(cut.Instance, "restaurants")!);
        Assert.Empty(
            (List<TableResponse>)
                typeof(Home).GetProperty("FilteredTables", PrivateInstance)!.GetValue(cut.Instance)!
        );
        Assert.Null(Get(cut.Instance, "selectedZoneId"));
    }

    [Theory]
    [InlineData("Borrar", "12,34", false, true, "0", 0)]
    [InlineData(",", "5", true, true, "0,", 0)]
    [InlineData(",", "1,2", false, true, "1,2", 1.2)]
    [InlineData(",", "1", false, true, "1,", 1)]
    [InlineData("8", "0", false, true, "8", 8)]
    [InlineData("4", "1,23", false, false, "1,234", 0)]
    [InlineData("1", "1234567890", false, false, "12345678901", 0)]
    [InlineData("9", "999999", false, false, "9999999", 9999999)]
    public void Payment_keypad_builds_values_and_rejects_invalid_precision_or_amount(
        string key,
        string currentText,
        bool startNew,
        bool expectedValid,
        string expectedText,
        double expectedAmount
    )
    {
        Home page = new();
        Set(page, "paymentKeypadText", currentText);
        Set(page, "paymentKeypadStartNew", startNew);
        MethodInfo build = typeof(Home).GetMethod("TryBuildPaymentKeypadValue", PrivateInstance)!;
        object?[] arguments = [key, null, null];

        bool valid = (bool)build.Invoke(page, arguments)!;

        Assert.Equal(expectedValid, valid);
        Assert.Equal(expectedText, arguments[1]);
        Assert.Equal((decimal)expectedAmount, arguments[2]);
    }

    private static MenuItemResponse MenuItem(
        string categoryName,
        string categoryCode,
        IReadOnlyList<MenuItemFormatPrice> formats,
        bool isAvailable = true
    ) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"SKU-{categoryCode}",
            "Producto",
            Guid.NewGuid(),
            categoryCode,
            categoryName,
            formats[0].Price,
            isAvailable,
            categoryCode,
            categoryName,
            DefaultFormat: formats[0].Format,
            Formats: formats
        );

    [Fact]
    public async Task Checkout_captures_own_orders_and_refreshes_the_session_bill()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        List<JsonDocument> checkoutRequests = [];
        bool saleReady = false;
        SessionBillResponse paidBill = new(
            sessionId,
            restaurantId,
            Guid.NewGuid(),
            "Open",
            20m,
            0m,
            [
                new(
                    orderId,
                    "Submitted",
                    "Paid",
                    20m,
                    20m,
                    DateTime.UtcNow,
                    Source: "Comandero",
                    CreatedByUserId: userId
                ),
            ]
        );
        HttpClient http = new(
            new CheckoutHandler(async request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/api/identity/auth/provider")
                    return Json(new ProviderSettingsResponse("", false, false));
                if (path == $"/api/dining/sessions/{sessionId}/checkout")
                {
                    checkoutRequests.Add(
                        await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync())
                    );
                    return Json(paidBill);
                }
                if (path == $"/api/dining/sessions/{sessionId}/bill")
                    return Json(paidBill);
                if (path == $"/api/orders/{orderId}")
                    return Json(
                        new OrderDetailResponse(orderId, restaurantId, "Submitted", 20m, [], [])
                    );
                if (path == $"/api/sales/{sessionId}" && saleReady)
                    return Json(new SaleResponse(PaymentMethod: "Card"));
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<Home> cut = Render<Home>();
        LoginResponse login = new(
            "token",
            DateTime.UtcNow.AddHours(1),
            new LoginUser(userId, "user", "Usuario"),
            []
        );
        Set(cut.Instance, "login", login);
        Set(
            cut.Instance,
            "diningSession",
            new DiningSessionResponse(sessionId, restaurantId, Guid.NewGuid(), "Open", userId)
        );
        Set(cut.Instance, "sessionId", sessionId);
        Set(
            cut.Instance,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                Guid.NewGuid(),
                "Open",
                20m,
                20m,
                [
                    new(
                        orderId,
                        "Submitted",
                        "Unpaid",
                        20m,
                        0m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ]
            )
        );
        Set(cut.Instance, "currentChargeAmount", 20m);
        Set(cut.Instance, "CurrentTipAmount", 2m);
        Set(cut.Instance, "paymentReference", "  REFERENCIA  ");
        Set(cut.Instance, "paymentMethod", "Card");
        Set(cut.Instance, "paymentDialogOpen", true);

        MethodInfo checkout = typeof(Home).GetMethod("CheckoutSession", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)checkout.Invoke(cut.Instance, null)!);

        JsonElement request = Assert.Single(checkoutRequests).RootElement;
        Assert.Equal("Card", request.GetProperty("method").GetString());
        Assert.Equal("REFERENCIA", request.GetProperty("externalReference").GetString());
        Assert.Equal(2m, request.GetProperty("tipAmount").GetDecimal());
        Assert.Equal(22m, request.GetProperty("amountReceived").GetDecimal());
        JsonElement allocation = Assert.Single(request.GetProperty("allocations").EnumerateArray());
        Assert.Equal(orderId, allocation.GetProperty("orderId").GetGuid());
        Assert.Equal(20m, allocation.GetProperty("amount").GetDecimal());
        Assert.False((bool)Get(cut.Instance, "paymentDialogOpen")!);
        Assert.True((bool)Get(cut.Instance, "success")!);
        Assert.Equal(Guid.Empty, Get(cut.Instance, "checkoutKey"));
        string message = (string)Get(cut.Instance, "message")!;
        Assert.Contains("Cobro con tarjeta de", message);
        Assert.Contains("€ registrado", message);
        Assert.Null(Get(cut.Instance, "readyTicketSessionId"));

        saleReady = true;
        MethodInfo completed = typeof(Home).GetMethod("HandleSaleCompleted", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)completed.Invoke(cut.Instance, [sessionId])!);
        Assert.Equal(sessionId, Get(cut.Instance, "readyTicketSessionId"));
    }

    [Fact]
    public async Task Selecting_available_table_opens_session_and_requests_guest_count()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/api/identity/auth/provider")
                    return Task.FromResult(Json(new ProviderSettingsResponse("", false, false)));
                if (path == $"/api/dining/tables/{tableId}/sessions")
                    return Task.FromResult(
                        Json(
                            new DiningSessionResponse(
                                sessionId,
                                restaurantId,
                                tableId,
                                "Open",
                                RequestGuestCount: true
                            )
                        )
                    );
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "active-token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "user", "Usuario"),
                []
            )
        );
        TableResponse table = new(
            tableId,
            restaurantId,
            "M01",
            "Mesa 1",
            true,
            "Available",
            null,
            Guid.NewGuid(),
            "Terraza",
            1
        );
        Set(cut.Instance, "restaurantId", restaurantId);

        MethodInfo selectTable = typeof(Home).GetMethod("SelectTable", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)selectTable.Invoke(cut.Instance, [table])!);

        Assert.Equal(sessionId, ((DiningSessionResponse)Get(cut.Instance, "diningSession")!).Id);
        Assert.True((bool)Get(cut.Instance, "guestCountDialogOpen")!);
        Assert.Same(table, Get(cut.Instance, "guestCountTable"));
        Assert.Null(Get(cut.Instance, "selectedTable"));
        cut.Render();
        Assert.Contains("¿Cuántos comensales?", cut.Markup);
        cut.Find("button[aria-label='Agregar un comensal']").Click();
        Assert.Contains("3", cut.Find(".guest-count-stepper strong").TextContent);
        cut.FindAll(".guest-count-options button")
            .Single(button => button.TextContent == "8")
            .Click();
        Assert.Contains("8", cut.Find(".guest-count-stepper strong").TextContent);
        Set(cut.Instance, "guestCountSessionOpenedHere", false);
        cut.Find(".guest-count-actions button.ghost").Click();
        Assert.False((bool)Get(cut.Instance, "guestCountDialogOpen")!);
    }

    [Fact]
    public async Task Selecting_occupied_table_uses_active_session_and_loads_menu()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DiningSessionResponse session = new(
            sessionId,
            restaurantId,
            tableId,
            "Open",
            RequestGuestCount: true,
            GuestCount: 3
        );
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/api/identity/auth/provider")
                    return Task.FromResult(Json(new ProviderSettingsResponse("", false, false)));
                if (path == $"/api/dining/tables/{tableId}/active-session")
                    return Task.FromResult(Json(session));
                if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
                    return Task.FromResult(Json(Array.Empty<MenuItemResponse>()));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        TableResponse table = new(
            tableId,
            restaurantId,
            "M01",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            Guid.NewGuid(),
            "Terraza",
            3
        );
        Set(cut.Instance, "restaurantId", restaurantId);

        MethodInfo selectTable = typeof(Home).GetMethod("SelectTable", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)selectTable.Invoke(cut.Instance, [table])!);

        Assert.Equal(sessionId, ((DiningSessionResponse)Get(cut.Instance, "diningSession")!).Id);
        Assert.False((bool)Get(cut.Instance, "guestCountDialogOpen")!);
        Assert.Same(table, Get(cut.Instance, "selectedTable"));
        Assert.Empty((List<MenuItemResponse>)Get(cut.Instance, "menu")!);
    }

    [Fact]
    public async Task Send_order_creates_and_submits_the_cart_then_refreshes_active_orders()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        List<string> requests = [];
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add($"{request.Method} {path}");
                if (path == "/api/identity/auth/provider")
                    return Task.FromResult(Json(new ProviderSettingsResponse("", false, false)));
                if (path == "/api/orders" && request.Method == HttpMethod.Post)
                    return Task.FromResult(
                        Json(
                            new OrderResponse(
                                orderId,
                                restaurantId,
                                tableId,
                                sessionId,
                                "",
                                "DineIn",
                                "OnAccount",
                                "Draft",
                                24m,
                                DateTime.UtcNow
                            )
                        )
                    );
                if (path == $"/api/orders/{orderId}/submit")
                    return Task.FromResult(
                        Json(
                            new OrderResponse(
                                orderId,
                                restaurantId,
                                tableId,
                                sessionId,
                                "",
                                "DineIn",
                                "OnAccount",
                                "Submitted",
                                24m,
                                DateTime.UtcNow
                            )
                        )
                    );
                if (path == $"/api/orders/commander?restaurantId={restaurantId}")
                    return Task.FromResult(
                        Json(
                            new[]
                            {
                                new OrderResponse(
                                    orderId,
                                    restaurantId,
                                    tableId,
                                    sessionId,
                                    "",
                                    "DineIn",
                                    "OnAccount",
                                    "Submitted",
                                    24m,
                                    DateTime.UtcNow
                                ),
                            }
                        )
                    );
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        TableResponse table = new(
            tableId,
            restaurantId,
            "M01",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            Guid.NewGuid(),
            "Terraza",
            1
        );
        Set(cut.Instance, "restaurantId", restaurantId);
        Set(cut.Instance, "selectedTable", table);
        Set(cut.Instance, "sessionId", sessionId);
        Set(
            cut.Instance,
            "diningSession",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Open")
        );
        Set(
            cut.Instance,
            "cart",
            new List<CommanderCartLine>
            {
                new()
                {
                    ProductId = productId,
                    Format = "PLATO",
                    BasePrice = 12m,
                    Quantity = 2,
                },
            }
        );
        Set(cut.Instance, "notes", new Dictionary<Guid, string> { [productId] = "sin cebolla" });

        MethodInfo sendOrder = typeof(Home).GetMethod("SendOrder", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)sendOrder.Invoke(cut.Instance, null)!);

        Assert.Equal(
            [
                "GET /api/identity/auth/provider",
                "POST /api/orders",
                $"POST /api/orders/{orderId}/submit",
                $"GET /api/orders/commander?restaurantId={restaurantId}",
            ],
            requests
        );
        Assert.Empty((List<CommanderCartLine>)Get(cut.Instance, "cart")!);
        Assert.Equal(
            orderId,
            Assert.Single((List<OrderResponse>)Get(cut.Instance, "activeOrders")!).Id
        );
        Assert.True((bool)Get(cut.Instance, "success")!);
        Assert.Contains("enviado a cocina", (string)Get(cut.Instance, "message")!);
        Assert.False((bool)Get(cut.Instance, "busy")!);
    }

    [Fact]
    public async Task Release_paid_session_closes_it_and_refreshes_table_state()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        List<string> requests = [];
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add($"{request.Method} {path}");
                if (path == "/api/identity/auth/provider")
                    return Task.FromResult(Json(new ProviderSettingsResponse("", false, false)));
                if (path == $"/api/dining/sessions/{sessionId}/close")
                    return Task.FromResult(
                        Json(new DiningSessionResponse(sessionId, restaurantId, tableId, "Closed"))
                    );
                if (path == $"/api/dining/restaurants/{restaurantId}/tables")
                    return Task.FromResult(Json(Array.Empty<TableResponse>()));
                if (path == $"/api/orders/commander?restaurantId={restaurantId}")
                    return Task.FromResult(Json(Array.Empty<OrderResponse>()));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        Set(cut.Instance, "restaurantId", restaurantId);
        Set(cut.Instance, "sessionId", sessionId);
        Set(
            cut.Instance,
            "diningSession",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Open")
        );
        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(userId, "user", "Usuario"),
                []
            )
        );
        Set(
            cut.Instance,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                tableId,
                "Open",
                10m,
                0m,
                [
                    new(
                        Guid.NewGuid(),
                        "Submitted",
                        "Paid",
                        10m,
                        10m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ],
                CanRelease: true
            )
        );

        MethodInfo release = typeof(Home).GetMethod("ReleasePaidSession", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)release.Invoke(cut.Instance, null)!);

        Assert.Equal(
            [
                "GET /api/identity/auth/provider",
                $"POST /api/dining/sessions/{sessionId}/close",
                $"GET /api/dining/restaurants/{restaurantId}/tables",
                $"GET /api/orders/commander?restaurantId={restaurantId}",
            ],
            requests
        );
        Assert.Null(Get(cut.Instance, "diningSession"));
        Assert.Null(Get(cut.Instance, "selectedTable"));
        Assert.True((bool)Get(cut.Instance, "success")!);
        Assert.Equal("Mesa liberada.", Get(cut.Instance, "message"));
        Assert.False((bool)Get(cut.Instance, "busy")!);
    }

    [Fact]
    public async Task Ticket_button_becomes_available_only_after_the_sale_is_read()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        int saleReads = 0;
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/identity/auth/provider")
                    return Task.FromResult(Json(new ProviderSettingsResponse("", false, false)));
                if (request.RequestUri.AbsolutePath == $"/api/sales/{sessionId}")
                {
                    saleReads++;
                    return Task.FromResult(Json(new SaleResponse(PaymentMethod: "Card")));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        Set(cut.Instance, "restaurantId", restaurantId);
        Set(cut.Instance, "sessionId", sessionId);
        Set(
            cut.Instance,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                Guid.NewGuid(),
                "Open",
                12m,
                0m,
                [new(Guid.NewGuid(), "Submitted", "Paid", 12m, 12m, DateTime.UtcNow)]
            )
        );
        MethodInfo refresh = typeof(Home).GetMethod("RefreshSalesTicketAsync", PrivateInstance)!;
        await cut.InvokeAsync(() => (Task)refresh.Invoke(cut.Instance, [sessionId])!);

        Assert.Equal(sessionId, Get(cut.Instance, "readyTicketSessionId"));
        Assert.IsType<SaleResponse>(Get(cut.Instance, "salesTicket"));
        Assert.Equal(1, saleReads);
        await cut.InvokeAsync(() =>
            (Task)
                typeof(Home)
                    .GetMethod("OpenSalesTicket", PrivateInstance)!
                    .Invoke(cut.Instance, null)!
        );
        Assert.True((bool)Get(cut.Instance, "salesTicketOpen")!);
    }

    [Fact]
    public async Task Ticket_refresh_handles_missing_failed_and_stale_sales()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid staleSessionId = Guid.NewGuid();
        TaskCompletionSource<HttpResponseMessage> pendingResponse = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int saleReads = 0;
        HttpClient http = new(
            new CheckoutHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/identity/auth/provider")
                    return Task.FromResult(Json(new ProviderSettingsResponse("", false, false)));
                saleReads++;
                return saleReads switch
                {
                    1 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                    2 => Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    ),
                    _ => pendingResponse.Task,
                };
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        IRenderedComponent<Home> cut = Render<Home>();
        Set(cut.Instance, "sessionId", sessionId);
        MethodInfo refresh = typeof(Home).GetMethod("RefreshSalesTicketAsync", PrivateInstance)!;

        await cut.InvokeAsync(() => (Task)refresh.Invoke(cut.Instance, [sessionId])!);
        Assert.Null(Get(cut.Instance, "readyTicketSessionId"));
        Assert.Null(Get(cut.Instance, "salesTicket"));

        await cut.InvokeAsync(() => (Task)refresh.Invoke(cut.Instance, [sessionId])!);
        Assert.Contains("503", (string)Get(cut.Instance, "ticketAvailabilityError")!);
        Assert.Null(Get(cut.Instance, "readyTicketSessionId"));

        Task staleRefresh = (Task)refresh.Invoke(cut.Instance, [sessionId])!;
        Set(cut.Instance, "sessionId", staleSessionId);
        pendingResponse.SetResult(Json(new SaleResponse(PaymentMethod: "Card")));
        await staleRefresh;

        Assert.Equal(3, saleReads);
        Assert.Equal(staleSessionId, Get(cut.Instance, "sessionId"));
        Assert.Null(Get(cut.Instance, "readyTicketSessionId"));
        Assert.Null(Get(cut.Instance, "salesTicket"));
    }

    [Fact]
    public void Commander_renders_zones_tables_menu_consumption_ticket_and_payment_dialogs()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        HttpClient http = new(
            new CheckoutHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<Home> cut = Render<Home>();
        Set(cut.Instance, "initializing", false);
        Set(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                now.AddHours(1),
                new LoginUser(userId, "user", "Usuario"),
                []
            )
        );
        Set(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new(
                    restaurantId,
                    "CENTRO",
                    "Restaurante Centro",
                    "Calle Mayor 1",
                    true,
                    new RestaurantCompanyResponse(
                        Guid.NewGuid(),
                        "Restaurante Centro SL",
                        "B12345678",
                        "Calle Mayor 1",
                        "910000000",
                        "centro@example.test"
                    )
                ),
            }
        );
        Set(cut.Instance, "restaurantId", restaurantId);
        TableResponse table = new(
            tableId,
            restaurantId,
            "M01",
            "Mesa 1",
            true,
            "Available",
            null,
            zoneId,
            "Terraza",
            1
        );
        Guid currentSessionId = Guid.NewGuid();
        Guid otherSessionId = Guid.NewGuid();
        TableResponse currentCommanderTable = table with
        {
            Id = Guid.NewGuid(),
            Code = "M02",
            Label = "Mesa propia",
            Status = "Occupied",
            ActiveSessionId = currentSessionId,
            ActiveSessionOpenedByUserId = userId,
            ActiveSessionSource = "Comandero",
        };
        TableResponse otherCommanderTable = table with
        {
            Id = Guid.NewGuid(),
            Code = "M03",
            Label = "Mesa de otro usuario",
            Status = "Occupied",
            ActiveSessionId = otherSessionId,
            ActiveSessionOpenedByUserId = Guid.NewGuid(),
            ActiveSessionSource = "Comandero",
        };
        TableResponse otherZoneTable = table with
        {
            Id = Guid.NewGuid(),
            Code = "T01",
            Label = "Mesa exterior",
            ZoneId = Guid.NewGuid(),
            ZoneName = "Exterior",
            ZoneSortOrder = 2,
        };
        Set(
            cut.Instance,
            "FilteredTables",
            new List<TableResponse>
            {
                table,
                currentCommanderTable,
                otherCommanderTable,
                otherZoneTable,
            }
        );
        cut.Render();

        Assert.Contains("Seleccionar ubicación", cut.Markup);
        Assert.Contains("Terraza", cut.Markup);
        Assert.Contains("1 disponibles", cut.Markup);
        Assert.Contains("Exterior", cut.Markup);

        Set(cut.Instance, "selectedZoneId", zoneId);
        cut.Render();
        Assert.Contains("Mesa 1", cut.Markup);
        Assert.Contains("Disponible · abrir", cut.Markup);
        Assert.Contains("Mesa propia", cut.Find(".table-card.occupied").TextContent);
        Assert.Contains("Mesa de otro usuario", cut.Find(".table-card.occupied-other").TextContent);
        Assert.Contains("Mesa 1", cut.Find(".table-card.available").TextContent);
        Assert.DoesNotContain("Mesa exterior", cut.Markup);

        Set(cut.Instance, "selectedTable", table);
        Set(cut.Instance, "sessionId", sessionId);
        Set(cut.Instance, "guestCount", 2);
        Set(
            cut.Instance,
            "diningSession",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Open", userId)
        );
        Set(
            cut.Instance,
            "menu",
            new List<MenuItemResponse>
            {
                new(
                    restaurantId,
                    productId,
                    "BURG-1",
                    "Hamburguesa de prueba",
                    Guid.NewGuid(),
                    "COMIDA",
                    "Platos",
                    12m,
                    true,
                    "CARNES",
                    "Carnes"
                ),
            }
        );
        cut.Render();
        Assert.Contains("Pedido en curso", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hamburguesa de prueba", cut.Markup);
        Assert.Contains("Enviar a cocina", cut.Markup);

        OrderLineDetailResponse line = new(
            Guid.NewGuid(),
            "Hamburguesa de prueba",
            12m,
            1,
            12m,
            "CARNES",
            "Carnes",
            "Active",
            null,
            "",
            KitchenStatus: "Pending",
            Format: "PLATO",
            BaseUnitPrice: 12m,
            Extras: [new(Guid.NewGuid(), "Queso", 1.5m, 1)]
        );
        OrderLineDetailResponse cancelledLine = new(
            Guid.NewGuid(),
            "Plato cancelado",
            4m,
            1,
            4m,
            "CARNES",
            "Carnes",
            "Cancelled",
            now,
            "Sin existencias"
        );
        Set(
            cut.Instance,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                tableId,
                "Open",
                13.5m,
                13.5m,
                [
                    new SessionBillOrderResponse(
                        orderId,
                        "Submitted",
                        "Unpaid",
                        13.5m,
                        0m,
                        now,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ],
                CanRelease: true,
                GuestCount: 2,
                PaymentMethod: "Card",
                TipTotal: 1m
            )
        );
        Set(
            cut.Instance,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [orderId] = new(
                    orderId,
                    restaurantId,
                    "Submitted",
                    13.5m,
                    [line, cancelledLine],
                    []
                ),
            }
        );
        Set(cut.Instance, "billOpen", true);
        cut.Render();
        Assert.Contains("CONSUMO DE LA MESA", cut.Markup);
        Assert.Contains("Queso", cut.Markup);
        Assert.Contains("Cancelado · Sin existencias", cut.Markup);
        Assert.Contains("Pendiente de tus comandas", cut.Markup);
        Assert.Contains("Liberar mesa", cut.Markup);
        Assert.Contains("Cancelar", cut.Markup);

        Set(cut.Instance, "billOpen", false);
        Set(cut.Instance, "salesTicketOpen", true);
        cut.Render();
        Assert.Contains("TICKET DE VENTA", cut.Markup);
        Assert.Contains("Restaurante Centro SL", cut.Markup);
        Assert.Contains("centro@example.test", cut.Markup);
        Set(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new(restaurantId, "LOCAL", "Local", "Calle Central 2", true),
            }
        );
        Set(
            cut.Instance,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                tableId,
                "Closed",
                3.5m,
                0m,
                [
                    new(Guid.NewGuid(), "Cancelled", "Refunded", 8m, 8m, now),
                    new(orderId, "Delivered", "Paid", 3.5m, 3.5m, now),
                ],
                GuestCount: null,
                PaymentMethod: "Cash",
                PaidAtUtc: now,
                DiscountPercentage: 10m,
                DiscountAmount: 0.35m
            )
        );
        Set(
            cut.Instance,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [orderId] = new(
                    orderId,
                    restaurantId,
                    "Delivered",
                    3.5m,
                    [
                        new(
                            Guid.NewGuid(),
                            "Agua",
                            3.5m,
                            1,
                            3.5m,
                            "DRINKS",
                            "Bebidas",
                            "Active",
                            null,
                            "",
                            TaxRate: 10m
                        ),
                    ],
                    []
                ),
            }
        );
        Set(
            cut.Instance,
            "salesTicket",
            new SaleResponse(
                "Card",
                PaymentMethods: [new SalePaymentBreakdown("Card", 1, 3.15m)],
                DiscountName: "Promoción",
                DiscountPercentage: 10m,
                DiscountAmount: 0.35m
            )
        );
        cut.Render();

        Assert.Contains("Calle Central 2", cut.Markup);
        Assert.Contains("Agua", cut.Markup);
        Assert.Contains("Base imponible (10 %)", cut.Markup);
        Assert.Contains("Descuento (10 %)", cut.Markup);
        Assert.Contains("Medio de pago · Tarjeta", cut.Markup);
        Assert.Contains("Total (IVA incluido)", cut.Markup);

        Set(cut.Instance, "salesTicket", null);
        Set(cut.Instance, "salesTicketOpen", false);
        Set(
            cut.Instance,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                tableId,
                "Closed",
                3.5m,
                0m,
                [new(orderId, "Delivered", "Paid", 3.5m, 3.5m, now)],
                PaymentMethod: "Cash",
                PaidAtUtc: now
            )
        );
        Set(cut.Instance, "salesTicketOpen", true);
        cut.Render();
        Assert.Contains("Calle Central 2", cut.Markup);
        Assert.Contains("Medio de pago", cut.Markup);
        Assert.Contains("Efectivo", cut.Markup);
        Set(cut.Instance, "salesTicketOpen", false);
        Set(cut.Instance, "billOpen", true);
        Set(cut.Instance, "paymentDialogOpen", false);
        Set(
            cut.Instance,
            "bill",
            new SessionBillResponse(
                sessionId,
                restaurantId,
                tableId,
                "Closed",
                3.5m,
                0m,
                [new(orderId, "Delivered", "Paid", 3.5m, 3.5m, now)],
                PaymentMethod: "Cash",
                PaidAtUtc: now
            )
        );
        Set(cut.Instance, "salesTicket", null);
        Set(cut.Instance, "readyTicketSessionId", null);
        Set(cut.Instance, "ticketAvailabilityError", null);
        cut.Render();
        Assert.Contains("Preparando ticket de venta", cut.Markup);

        Set(cut.Instance, "ticketAvailabilityError", "API 503");
        cut.Render();
        Assert.Contains("No se pudo comprobar el ticket", cut.Markup);

        Set(cut.Instance, "salesTicket", new SaleResponse("Card"));
        Set(cut.Instance, "readyTicketSessionId", sessionId);
        cut.Render();
        Assert.Contains("Ver ticket", cut.Markup);
        Set(cut.Instance, "salesTicketOpen", true);
        cut.Render();
        string ticketText = cut.Find(".sales-ticket").TextContent;
        Assert.Contains("Calle Central 2", ticketText);
        Assert.Contains("Tarjeta / datáfono", ticketText);
        Assert.DoesNotContain("Restaurante Centro SL", ticketText);

        Set(cut.Instance, "salesTicketOpen", false);
        Set(cut.Instance, "paymentDialogOpen", true);
        Set(cut.Instance, "currentChargeAmount", 13.5m);
        cut.Render();
        Assert.Contains("payment-dialog-title", cut.Markup);
        Assert.Contains("Teclado numérico", cut.Markup);
        Assert.Contains("Pendiente tras este cobro", cut.Markup);
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value, JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json"
            ),
        };

    private static void Set(object instance, string name, object? value)
    {
        FieldInfo? field = typeof(Home).GetField(name, PrivateInstance);
        if (field is not null)
        {
            field.SetValue(instance, value);
            return;
        }

        typeof(Home).GetProperty(name, PrivateInstance)!.SetValue(instance, value);
    }

    private static object? Get(object instance, string name) =>
        typeof(Home).GetField(name, PrivateInstance)!.GetValue(instance);

    private sealed class CheckoutHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => reply(request);
    }

    private sealed class EmptyJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class SessionStorageJsRuntime(string storedSession) : IJSRuntime
    {
        public bool RemovedSession { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "sessionStorage.getItem")
            {
                return ValueTask.FromResult((TValue)(object)storedSession);
            }
            if (identifier == "sessionStorage.removeItem")
            {
                RemovedSession = true;
            }
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
    }
}
