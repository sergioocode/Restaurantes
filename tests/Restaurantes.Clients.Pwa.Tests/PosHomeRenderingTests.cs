using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Pos.Pwa.Realtime;
using Xunit;
using PosHome = Restaurantes.Clients.Pos.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class PosHomeRenderingTests : BunitContext
{
    public PosHomeRenderingTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
    }

    [Fact]
    public async Task Login_code_loads_the_allowed_restaurant_and_switching_local_reloads_its_tables()
    {
        await using WebApplication realtimeApp = await StartRealtimeHubAsync();
        TestServer realtimeServer = realtimeApp.GetTestServer();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        TableResponse firstTable = new(
            Guid.NewGuid(),
            firstId,
            "M1",
            "Mesa 1",
            true,
            "Available",
            null,
            Guid.NewGuid(),
            "Sala",
            1
        );
        TableResponse secondTable = firstTable with
        {
            Id = Guid.NewGuid(),
            RestaurantId = secondId,
            Label = "Mesa 2",
        };
        LoginBootstrapHandler handler = new(firstId, secondId, firstTable, secondTable);
        HttpClient http = new(handler) { BaseAddress = new Uri("https://test.local") };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api, realtimeServer.CreateHandler));
        Services.AddSingleton(new CatalogRealtimeClient(http, api, realtimeServer.CreateHandler));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.BaseUri + "#login_code=one-time-code");

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        Assert.Equal(firstId, GetField<Guid>(cut.Instance, "restaurantId"));
        Assert.Equal(
            firstTable.Id,
            Assert.Single(GetField<List<TableResponse>>(cut.Instance, "tables")).Id
        );
        Assert.Contains(handler.Paths, path => path == "/api/identity/auth/exchange");

        int pathsBeforeIgnoredChanges = handler.Paths.Count;
        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RestaurantChanged", new ChangeEventArgs { Value = "invalid" })
        );
        await cut.InvokeAsync(() =>
            InvokeTask(
                cut.Instance,
                "RestaurantChanged",
                new ChangeEventArgs { Value = firstId.ToString() }
            )
        );
        await cut.InvokeAsync(() =>
            InvokeTask(
                cut.Instance,
                "RestaurantChanged",
                new ChangeEventArgs { Value = Guid.NewGuid().ToString() }
            )
        );
        Assert.Equal(pathsBeforeIgnoredChanges, handler.Paths.Count);
        Assert.Equal(firstId, GetField<Guid>(cut.Instance, "restaurantId"));

        await cut.InvokeAsync(() =>
            InvokeTask(
                cut.Instance,
                "RestaurantChanged",
                new ChangeEventArgs { Value = secondId.ToString() }
            )
        );
        Assert.Equal(secondId, GetField<Guid>(cut.Instance, "restaurantId"));
        Assert.Equal(
            secondTable.Id,
            Assert.Single(GetField<List<TableResponse>>(cut.Instance, "tables")).Id
        );
    }

    [Fact]
    public async Task Login_error_fragment_shows_authorization_guidance()
    {
        HttpClient http = new(new ProviderSettingsHandler())
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton(new CatalogRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.BaseUri + "#other=value&login_error=access_denied");

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        Assert.Contains(
            "La cuenta no está autorizada o el proveedor rechazó el acceso.",
            GetField<string>(cut.Instance, "message")
        );
    }

    [Fact]
    public async Task LoadRestaurants_rejects_users_without_any_pos_restaurant()
    {
        Guid restaurantId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(
            restaurantId,
            [],
            new EmptyAccessibleRestaurantsHandler()
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                cut.InvokeAsync(() => InvokeTask(cut.Instance, "LoadRestaurants"))
        );

        Assert.Equal(
            "El usuario no tiene permisos para operar el TPV en ningún local.",
            exception.Message
        );
    }

    [Fact]
    public async Task Realtime_connection_failure_keeps_pos_loaded_and_reports_degraded_updates()
    {
        Guid restaurantId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);

        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "ConnectRealtimeAsync"));

        Assert.Contains(
            "Datos cargados, pero sin actualización en tiempo real:",
            GetField<string>(cut.Instance, "message")
        );
    }

    [Fact]
    public async Task Logout_removes_authentication_and_clears_restaurant_operational_state()
    {
        Guid restaurantId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        PosApi api = Services.GetRequiredService<PosApi>();
        CancellationTokenSource reconciliation = new();
        CancellationTokenSource kitchenClockRefresh = new();
        api.AccessToken = "session-token";
        SetField(cut.Instance, "reconciliation", reconciliation);
        SetField(cut.Instance, "kitchenClockRefresh", kitchenClockRefresh);
        SetField(
            cut.Instance,
            "tables",
            new List<TableResponse>
            {
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    "M1",
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
        SetField(cut.Instance, "selectedZoneId", Guid.NewGuid());
        SetField(cut.Instance, "pickupView", true);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "Logout"));

        Assert.Null(api.AccessToken);
        Assert.Null(GetField<LoginResponse?>(cut.Instance, "login"));
        Assert.Empty(GetField<List<RestaurantResponse>>(cut.Instance, "restaurants"));
        Assert.Empty(GetField<List<TableResponse>>(cut.Instance, "tables"));
        Assert.Null(GetField<Guid?>(cut.Instance, "selectedZoneId"));
        Assert.False(GetField<bool>(cut.Instance, "pickupView"));
        Assert.True(reconciliation.IsCancellationRequested);
        Assert.True(kitchenClockRefresh.IsCancellationRequested);
    }

    [Fact]
    public async Task Login_button_navigates_to_pos_auth_return_path()
    {
        IRenderedComponent<PosHome> cut = await RenderTakeaway(
            Guid.NewGuid(),
            [],
            new ProviderSettingsHandler()
        );
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "StartLogin", "Microsoft"));

        Assert.Contains("/api/identity/auth/start/Microsoft?returnPath=%2Fpos%2F", navigation.Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stored_session_is_restored_only_while_its_token_is_valid(bool expired)
    {
        await using WebApplication realtimeApp = await StartRealtimeHubAsync();
        TestServer realtimeServer = realtimeApp.GetTestServer();
        Guid restaurantId = Guid.NewGuid();
        TableResponse table = new(
            Guid.NewGuid(),
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Available",
            null,
            Guid.NewGuid(),
            "Sala",
            1
        );
        LoginBootstrapHandler handler = new(restaurantId, Guid.NewGuid(), table, table);
        HttpClient http = new(handler) { BaseAddress = new Uri("https://test.local") };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api, realtimeServer.CreateHandler));
        Services.AddSingleton(new CatalogRealtimeClient(http, api, realtimeServer.CreateHandler));
        LoginResponse login = new(
            "saved-token",
            expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddHours(1),
            new LoginUser(Guid.NewGuid(), "cajero", "Cajero"),
            []
        );
        StoredSessionJsRuntime js = new(JsonSerializer.Serialize(login));
        Services.AddSingleton<IJSRuntime>(js);

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        cut.WaitForState(() => !GetField<bool>(cut.Instance, "initializing"));

        if (expired)
        {
            Assert.True(js.Removed);
            Assert.Null(GetField<LoginResponse?>(cut.Instance, "login"));
            Assert.Empty(GetField<List<TableResponse>>(cut.Instance, "tables"));
        }
        else
        {
            Assert.Equal("saved-token", GetField<LoginResponse>(cut.Instance, "login").AccessToken);
            Assert.Equal(
                table.Id,
                Assert.Single(GetField<List<TableResponse>>(cut.Instance, "tables")).Id
            );
        }
        Assert.DoesNotContain(handler.Paths, path => path == "/api/identity/auth/exchange");
    }

    [Fact]
    public async Task Authenticated_pos_filters_tables_and_shows_available_catalog_items()
    {
        using HttpClient http = new(new EmptyHandler())
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton(new CatalogRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "cajero", "Cajero"),
                []
            )
        );
        SetField(cut.Instance, "initializing", false);
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new(restaurantId, "CENTRO", "Centro", "Calle Mayor", true),
            }
        );
        SetField(
            cut.Instance,
            "tables",
            new List<TableResponse>
            {
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    "M1",
                    "Mesa 1",
                    true,
                    "Available",
                    null,
                    zoneId,
                    "Salón",
                    1
                ),
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    "M2",
                    "Mesa 2",
                    true,
                    "Occupied",
                    null,
                    zoneId,
                    "Salón",
                    1
                ),
            }
        );

        await Rerender(cut);
        Assert.Contains("Salón", cut.Markup);
        cut.FindAll(".tables-toolbar button")
            .Single(button => button.TextContent.Contains("Salón"))
            .Click();
        Assert.Equal(2, cut.FindAll(".dining-location").Count);

        cut.FindAll(".table-status-filters button")
            .Single(button => button.TextContent == "Disponibles")
            .Click();
        Assert.Single(cut.FindAll(".dining-location"));
        Assert.Contains("Mesa 1", cut.Markup);
        Assert.DoesNotContain("Mesa 2", cut.Markup);

        SetField(
            cut.Instance,
            "tables",
            new List<TableResponse>
            {
                new(
                    Guid.NewGuid(),
                    restaurantId,
                    "M1",
                    "Mesa 1",
                    true,
                    "Available",
                    null,
                    zoneId,
                    "Salón",
                    1
                ),
            }
        );
        cut.FindAll(".table-status-filters button")
            .Single(button => button.TextContent == "Ocupadas")
            .Click();
        Assert.Contains("No hay ubicaciones para este filtro.", cut.Markup);

        SetField(cut.Instance, "tables", new List<TableResponse>());
        SetField(cut.Instance, "selectedZoneId", null!);
        await Rerender(cut);
        Assert.Contains("No hay sectores configurados", cut.Markup);

        SetField(
            cut.Instance,
            "menu",
            new List<MenuItemResponse>
            {
                new(
                    restaurantId,
                    Guid.NewGuid(),
                    "CAFE",
                    "Café",
                    Guid.NewGuid(),
                    "BEB",
                    "Bebidas",
                    2m,
                    true,
                    "BAR",
                    "Bar",
                    DefaultFormat: "UNIDAD"
                ),
                new(
                    restaurantId,
                    Guid.NewGuid(),
                    "AGUA",
                    "Agua",
                    Guid.NewGuid(),
                    "BEB",
                    "Bebidas",
                    1m,
                    false,
                    "BAR",
                    "Bar",
                    DefaultFormat: "UNIDAD"
                ),
            }
        );
        SetField(cut.Instance, "takeawayOpen", true);
        SetField(cut.Instance, "serviceMode", "Takeaway");
        await Rerender(cut);

        Assert.Contains("Café", cut.Markup);
        Assert.DoesNotContain("Agua", cut.Markup);
        Assert.Single(cut.FindAll(".product-tile"));
        Assert.Contains("Bebidas", cut.Markup);

        SetField(cut.Instance, "menu", new List<MenuItemResponse>());
        await Rerender(cut);
        Assert.Contains("No hay productos en este formato.", cut.Markup);
    }

    [Fact]
    public async Task Takeaway_cart_requires_extras_and_merges_matching_lines_after_editing()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        Guid cheeseId = Guid.NewGuid();
        Guid baconId = Guid.NewGuid();
        MenuItemResponse burger = new(
            restaurantId,
            Guid.NewGuid(),
            "BURGER",
            "Hamburguesa",
            Guid.NewGuid(),
            "FOOD",
            "Hamburguesas",
            10m,
            true,
            "GRILL",
            "Parrilla",
            ExtraGroups:
            [
                new ProductExtraGroup(
                    groupId,
                    "Complementos",
                    1,
                    2,
                    [
                        new ProductExtraOption(cheeseId, "CHEESE", "Queso", 0.5m),
                        new ProductExtraOption(baconId, "BACON", "Bacon", 1m),
                    ]
                ),
            ]
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [burger]);

        cut.Find(".product-tile").Click();
        Assert.True(cut.Find(".extras-card button.primary").HasAttribute("disabled"));
        cut.FindAll(".extra-option .stepper button")[1].Click();
        cut.FindAll(".extra-option .stepper button")[1].Click();
        cut.FindAll(".extra-option .stepper button")[1].Click();
        Assert.Equal("2", cut.FindAll(".extra-option .stepper span")[0].TextContent);
        cut.FindAll(".extra-option .stepper button")[0].Click();
        Assert.False(cut.Find(".extras-card button.primary").HasAttribute("disabled"));
        cut.Find(".extras-card button.primary").Click();
        Assert.Contains("1 × Queso", cut.Markup);
        Assert.Contains("10,50 €", cut.Markup);

        cut.Find(".product-tile").Click();
        cut.FindAll(".extra-option .stepper button")[3].Click();
        cut.Find(".extras-card button.primary").Click();
        Assert.Equal(2, cut.FindAll(".ticket-lines article").Count);

        cut.Find(".ticket-lines article:first-child .edit-extras").Click();
        cut.FindAll(".extra-option .stepper button")[0].Click();
        Assert.True(cut.Find(".extras-card button.primary").HasAttribute("disabled"));
        cut.FindAll(".extra-option .stepper button")[3].Click();
        cut.Find(".extras-card button.primary").Click();

        PosCartLine merged = Assert.Single(GetField<List<PosCartLine>>(cut.Instance, "cart"));
        Assert.Equal(2, merged.Quantity);
        Assert.Equal(11m, merged.ExpectedUnitPrice);
        Assert.Equal([new PosCartExtra(groupId, baconId, 1)], merged.Extras);
        Assert.Contains("22,00 €", cut.Markup);

        cut.Find(".ticket-lines article .stepper button:last-child").Click();
        Assert.Equal(3, merged.Quantity);
        cut.Find(".ticket-lines article .stepper button:first-child").Click();
        cut.Find(".ticket-lines article .stepper button:first-child").Click();
        cut.Find(".ticket-lines article .stepper button:first-child").Click();
        Assert.Empty(GetField<List<PosCartLine>>(cut.Instance, "cart"));
        Assert.Contains("Pulsa un producto para añadirlo.", cut.Markup);
    }

    [Fact]
    public async Task Menu_filters_use_category_defaults_and_selected_format_prices()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid beerCategoryId = Guid.NewGuid();
        Guid foodCategoryId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(
            restaurantId,
            [
                new(
                    restaurantId,
                    Guid.NewGuid(),
                    "BEER",
                    "Cerveza",
                    beerCategoryId,
                    "CERV",
                    "Cervezas",
                    2m,
                    true,
                    "BAR",
                    "Bar",
                    Formats:
                    [
                        new MenuItemFormatPrice("COPA", 3m),
                        new MenuItemFormatPrice("TERCIO", 4m),
                    ]
                ),
                new(
                    restaurantId,
                    Guid.NewGuid(),
                    "TORTILLA",
                    "Tortilla",
                    foodCategoryId,
                    "FOOD",
                    "Tapas",
                    8m,
                    true,
                    "KITCHEN",
                    "Cocina",
                    Formats:
                    [
                        new MenuItemFormatPrice("PLATO", 8m),
                        new MenuItemFormatPrice("TAPA", 4m),
                    ]
                ),
            ]
        );

        cut.FindAll(".category-tabs button")
            .Single(button => button.TextContent == "Cervezas")
            .Click();
        Assert.Single(cut.FindAll(".product-tile"));
        Assert.Contains("3,00 €", cut.Find(".product-tile").TextContent);
        cut.FindAll(".format-tabs button").Single(button => button.TextContent == "Tercio").Click();
        Assert.Contains("4,00 €", cut.Find(".product-tile").TextContent);

        cut.FindAll(".category-tabs button")
            .Single(button => button.TextContent == "Tapas")
            .Click();
        Assert.Equal("Tortilla", cut.Find(".product-tile strong").TextContent);
        Assert.Contains("8,00 €", cut.Find(".product-tile").TextContent);
        cut.FindAll(".format-tabs button").Single(button => button.TextContent == "Tapa").Click();
        Assert.Contains("4,00 €", cut.Find(".product-tile").TextContent);

        for (int quantity = 0; quantity < 100; quantity++)
        {
            await cut.Find(".product-tile").ClickAsync();
        }

        PosCartLine cartLine = Assert.Single(GetField<List<PosCartLine>>(cut.Instance, "cart"));
        Assert.Equal(99, cartLine.Quantity);
        Assert.Equal("TAPA", cartLine.Format);
        Assert.Equal(4m, cartLine.ExpectedUnitPrice);

        await cut.Find(".ticket-lines article .stepper button:last-child").ClickAsync();
        Assert.Equal(99, cartLine.Quantity);
    }

    [Fact]
    public async Task Payment_dialog_calculates_change_and_requires_exact_amount_for_transfer()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId) with { GuestCount = 2 };
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill);

        cut.Find(".session-bill-card .bill-actions button.primary").Click();
        Assert.Contains("2 comensales", cut.Find(".payment-dialog .bill-guest-count").TextContent);
        Assert.True(
            cut.Find(".payment-dialog .bill-actions button.primary").HasAttribute("disabled")
        );

        cut.FindAll(".payment-methods button")
            .Single(button => button.TextContent == "Efectivo")
            .Click();
        cut.Find(".payment-exact").Click();
        cut.FindAll(".tip-options button").Single(button => button.TextContent == "10%").Click();
        Assert.Contains("6,00 €", cut.FindAll(".payment-details .payment-value")[1].TextContent);

        cut.Find(".payment-keypad button[aria-label='7']").Click();
        cut.Find(".payment-keypad button[aria-label='0']").Click();
        Assert.Contains("70,00 €", cut.Find(".payment-value.active").TextContent);
        Assert.Contains("10,00 €", cut.Find(".bill-total.change").TextContent);

        cut.FindAll(".payment-methods button")
            .Single(button => button.TextContent == "Transferencia")
            .Click();
        Assert.Contains("60,00 €", cut.Find(".payment-details .payment-value").TextContent);
        Assert.True(cut.Find(".payment-exact").HasAttribute("disabled"));
        Assert.True(cut.Find(".payment-keypad button[aria-label='7']").HasAttribute("disabled"));

        cut.FindAll(".payment-methods button")
            .Single(button => button.TextContent == "Tarjeta")
            .Click();
        Assert.Contains("0,00 €", cut.Find(".payment-details .payment-value").TextContent);
        Assert.True(
            cut.Find(".payment-dialog .bill-actions button.primary").HasAttribute("disabled")
        );
    }

    [Fact]
    public async Task Payment_dialog_opens_only_for_a_chargeable_bill_and_cannot_close_while_busy()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill);

        SetField(cut.Instance, "busy", true);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "OpenPaymentDialog"));
        Assert.False(GetField<bool>(cut.Instance, "paymentDialogOpen"));

        SetField(cut.Instance, "busy", false);
        SetField(cut.Instance, "bill", bill with { Outstanding = 0m, CanCheckout = false });
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "OpenPaymentDialog"));
        Assert.False(GetField<bool>(cut.Instance, "paymentDialogOpen"));

        SetField(cut.Instance, "bill", bill);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "OpenPaymentDialog"));
        Assert.True(GetField<bool>(cut.Instance, "paymentDialogOpen"));

        SetField(cut.Instance, "busy", true);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "ClosePaymentDialog"));
        Assert.True(GetField<bool>(cut.Instance, "paymentDialogOpen"));

        SetField(cut.Instance, "busy", false);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "ClosePaymentDialog"));
        Assert.False(GetField<bool>(cut.Instance, "paymentDialogOpen"));
    }

    [Fact]
    public async Task Discount_dialog_rejects_invalid_values_and_applies_or_removes_discount()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse initial = Bill(restaurantId) with
        {
            DiscountPercentage = 5m,
            DiscountAmount = 3m,
            Outstanding = 57m,
        };
        DiscountHandler handler = new(initial);
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, initial, handler);

        cut.FindAll(".session-bill-card .bill-actions button")
            .Single(button => button.TextContent == "Descuento")
            .Click();
        cut.Find("#discount-percentage-input").Change("100");
        cut.Find(".discount-dialog button.primary").Click();
        Assert.Contains("no superar el 99,99", cut.Markup);
        Assert.Equal(0, handler.Updates);

        cut.Find("#discount-percentage-input").Change("no es un número");
        cut.Find(".discount-dialog button.primary").Click();
        Assert.Contains("Indica un porcentaje válido", cut.Markup);
        Assert.Equal(0, handler.Updates);

        cut.Find("#discount-percentage-input").Change("12.5");
        cut.Find(".discount-dialog button.primary").Click();
        Assert.Equal(12.5m, handler.LastPercentage);
        Assert.Contains("Descuento aplicado", cut.Markup);
        Assert.Contains("Descuento (12,5 %)", cut.Markup);

        cut.FindAll(".session-bill-card .bill-actions button")
            .Single(button => button.TextContent == "Descuento")
            .Click();
        cut.Find(".discount-dialog .discount-no-discount").Click();
        Assert.Equal(0m, handler.LastPercentage);
        Assert.Contains("Descuento quitado", cut.Markup);

        cut.FindAll(".session-bill-card .bill-actions button")
            .Single(button => button.TextContent == "Descuento")
            .Click();
        cut.Find(".discount-dialog button.ghost").Click();
        Assert.False(GetField<bool>(cut.Instance, "discountDialogOpen"));
        Assert.Equal(2, handler.Updates);
    }

    [Fact]
    public async Task Applying_discount_skips_invalid_busy_or_missing_session_and_handles_noop()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        DiscountHandler handler = new(bill);
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill, handler);
        Guid originalBillId = GetField<SessionBillResponse>(cut.Instance, "bill").Id;

        SetField(cut.Instance, "busy", true);
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "ApplyDiscountPercentage", 10m));
        Assert.Equal(0, handler.Updates);
        Assert.Equal(originalBillId, GetField<SessionBillResponse>(cut.Instance, "bill").Id);

        SetField(cut.Instance, "busy", false);
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "ApplyDiscountPercentage", -1m));
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "ApplyDiscountPercentage", 100m));
        Assert.Equal(0, handler.Updates);

        SetField(cut.Instance, "session", null!);
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "ApplyDiscountPercentage", 10m));
        Assert.Equal(0, handler.Updates);

        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(bill.Id, restaurantId, bill.TableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "discountDialogOpen", true);
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "ApplyDiscountPercentage", 0m));

        Assert.Equal(0, handler.Updates);
        Assert.False(GetField<bool>(cut.Instance, "discountDialogOpen"));
        Assert.True(GetField<bool>(cut.Instance, "success"));
        Assert.Equal(
            "No hay ningún descuento aplicado.",
            GetField<string>(cut.Instance, "message")
        );
    }

    [Fact]
    public async Task Location_screen_renders_pickup_states_and_table_session_snapshots()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid pickupId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DateTime submittedAt = DateTime.UtcNow.AddMinutes(-4);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        OrderResponse pickup = new(
            pickupId,
            restaurantId,
            null,
            null,
            "Ana",
            "Takeaway",
            "OnAccount",
            "Submitted",
            12m,
            submittedAt
        );

        SetField(cut.Instance, "takeawayOpen", false);
        SetField(cut.Instance, "pickupView", true);
        SetField(cut.Instance, "pickupOrders", new List<OrderResponse> { pickup });
        SetField(
            cut.Instance,
            "pickupPaymentStatuses",
            new Dictionary<Guid, string> { [pickupId] = "Paid" }
        );
        cut.Render();
        Assert.Contains("Ana", cut.Markup);
        Assert.Contains("Pagado", cut.Markup);
        SetField(cut.Instance, "pickupPaymentStatuses", new Dictionary<Guid, string>());
        cut.Render();
        Assert.Contains("Sin sincronizar", cut.Markup);

        SetField(cut.Instance, "pickupOrders", new List<OrderResponse>());
        cut.Render();
        Assert.Contains("No hay pedidos pendientes de recogida", cut.Markup);

        TableResponse table = new(
            tableId,
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            zoneId,
            "Terraza",
            1,
            2
        );
        OrderResponse activeOrder = new(
            Guid.NewGuid(),
            restaurantId,
            tableId,
            sessionId,
            string.Empty,
            "DineIn",
            "OnAccount",
            "Submitted",
            18m,
            submittedAt,
            submittedAt,
            [new OrderStationProgressResponse("BAR", "Barra", "InPreparation")]
        );
        SetField(cut.Instance, "pickupView", false);
        SetField(cut.Instance, "selectedZoneId", zoneId);
        SetField(cut.Instance, "tables", new List<TableResponse> { table });
        SetField(cut.Instance, "activeOrders", new List<OrderResponse> { activeOrder });

        cut.Render();
        Assert.Contains("Actualizando comandas", cut.Markup);
        IDictionary snapshots = GetField<IDictionary>(cut.Instance, "tableSessionOrderSnapshots");
        Type snapshotType = typeof(PosHome).GetNestedType(
            "TableSessionOrderSnapshot",
            BindingFlags.NonPublic
        )!;
        ConstructorInfo snapshotConstructor = snapshotType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        )[0];
        snapshots[tableId] = snapshotConstructor.Invoke([
            sessionId,
            "CustomerQr",
            new List<DiningSessionOrderResponse> { new(Guid.NewGuid(), "Submitted", "Paid", 18m) },
            true,
        ]);
        cut.Render();

        Assert.Contains("QR", cut.Markup);
        Assert.Contains("Submitted", cut.Markup);
        Assert.Contains("Paid", cut.Markup);
        Assert.Contains("2 comensales", cut.Markup);
        Assert.Contains("Tiempo desde el envío a cocina", cut.Markup);

        snapshots[tableId] = snapshotConstructor.Invoke([
            sessionId,
            "Comandero",
            new List<DiningSessionOrderResponse>(),
            false,
        ]);
        cut.Render();
        Assert.Contains("Estado de comandas no disponible", cut.Markup);

        snapshots[tableId] = snapshotConstructor.Invoke([
            sessionId,
            "Pos",
            new List<DiningSessionOrderResponse>(),
            true,
        ]);
        cut.Render();
        Assert.Contains("1 en cola", cut.Markup);
        SetField(cut.Instance, "tables", new List<TableResponse>());
        SetField(cut.Instance, "selectedZoneId", zoneId);
        cut.Render();
        Assert.Contains("No hay ubicaciones para este filtro", cut.Markup);

        SetField(cut.Instance, "selectedZoneId", null);
        cut.Render();
        Assert.Contains("No hay sectores configurados", cut.Markup);

        SetField(cut.Instance, "tables", new List<TableResponse> { table });
        cut.Render();
        Assert.Contains("Selecciona un sector", cut.Markup);
    }

    [Fact]
    public async Task Cash_panel_and_close_ticket_render_payment_and_movement_reconciliation()
    {
        Guid restaurantId = Guid.NewGuid();
        DateTime openedAt = DateTime.UtcNow.AddHours(-4);
        DateTime closedAt = DateTime.UtcNow;
        List<CashMovementResponse> movements =
        [
            new(
                Guid.NewGuid(),
                "Income",
                "Cash",
                10m,
                "Cambio caja",
                openedAt,
                "Aportación de caja",
                "Cajera"
            ),
            new(
                Guid.NewGuid(),
                "Expense",
                "Card",
                5m,
                "Papel térmico",
                openedAt,
                "Compra de suministros",
                "Cajera"
            ),
        ];
        CashRegisterResponse cashRegister = new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Open",
            100m,
            300m,
            200m,
            10m,
            5m,
            300m,
            305m,
            304m,
            199m,
            -1m,
            openedAt,
            "Cajera",
            null,
            null,
            [new("Cash", 200m), new("Card", 100m)],
            [new("Cash", 205m), new("Card", 100m)],
            [],
            movements
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        SetField(cut.Instance, "cashRegister", cashRegister);
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(
            cut.Instance,
            "reconciliationAmounts",
            new Dictionary<string, decimal> { ["Cash"] = 205m, ["Card"] = 100m }
        );
        cut.Render();

        Assert.Contains("200,00 €", cut.Markup);
        Assert.Contains("Tarjeta / datáfono · esperado 100,00 €", cut.Markup);
        Assert.Contains("Diferencia total provisional", cut.Markup);

        SetField(cut.Instance, "cashCloseTicketOpen", true);
        SetField(
            cut.Instance,
            "cashRegister",
            cashRegister with
            {
                Status = "Closed",
                ClosedAtUtc = closedAt,
                ClosedByName = "Encargado",
                Difference = -1m,
                Reconciliations = [new("Cash", 205m, 204m, -1m), new("Card", 100m, 100m, 0m)],
            }
        );
        cut.Render();

        Assert.Contains("Arqueo cerrado", cut.Markup);
        Assert.Contains("Cambio caja", cut.Markup);
        Assert.Contains("Compra de suministros · Papel térmico", cut.Markup);
        Assert.Contains("Ingreso · Efectivo", cut.Markup);
        Assert.Contains("Egreso · Tarjeta / datáfono", cut.Markup);
        Assert.Contains("Descuadre total", cut.Markup);
        Assert.Contains("Encargado", cut.Markup);
    }

    [Fact]
    public async Task Payment_keypad_clears_amount_and_rejects_duplicate_decimal_and_extra_digits()
    {
        Guid restaurantId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, Bill(restaurantId));

        cut.Find(".session-bill-card .bill-actions button.primary").Click();
        cut.FindAll(".payment-methods button")
            .Single(button => button.TextContent == "Efectivo")
            .Click();
        cut.Find(".payment-exact").Click();
        cut.Find(".payment-keypad button[aria-label='Poner a cero el importe seleccionado']")
            .Click();
        cut.Find(".payment-keypad button[aria-label=',']").Click();
        cut.Find(".payment-keypad button[aria-label='1']").Click();
        cut.Find(".payment-keypad button[aria-label='2']").Click();
        cut.Find(".payment-keypad button[aria-label=',']").Click();
        cut.Find(".payment-keypad button[aria-label='3']").Click();

        Assert.Contains("0,12 €", cut.Find(".payment-value.active").TextContent);
    }

    [Fact]
    public async Task Payment_keypad_rejects_invalid_amounts_and_locks_transfer_charge()
    {
        Guid restaurantId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, Bill(restaurantId));
        SetField(cut.Instance, "keypadTarget", "Charge");
        SetField(cut.Instance, "keypadText", "0");
        SetField(cut.Instance, "keypadStartNew", true);

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", ","));
        Assert.Equal("0,", GetField<string>(cut.Instance, "keypadText"));
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", ","));
        Assert.Equal("0,", GetField<string>(cut.Instance, "keypadText"));

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "1"));
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "2"));
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "3"));
        Assert.Equal("0,12", GetField<string>(cut.Instance, "keypadText"));

        SetField(cut.Instance, "keypadStartNew", true);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "1000001"));
        Assert.Equal("0,12", GetField<string>(cut.Instance, "keypadText"));

        SetField(cut.Instance, "paymentMethod", "Transfer");
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "9"));
        Assert.Equal("0,12", GetField<string>(cut.Instance, "keypadText"));

        SetField(cut.Instance, "paymentMethod", "Cash");
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "Borrar"));
        Assert.Equal("0", GetField<string>(cut.Instance, "keypadText"));
        Assert.Equal(0m, GetField<decimal>(cut.Instance, "currentChargeAmount"));

        SetField(cut.Instance, "paymentMethod", "Transfer");
        SetField(cut.Instance, "keypadTarget", "Tip");
        SetField(cut.Instance, "keypadText", "0");
        SetField(cut.Instance, "keypadStartNew", true);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "x"));
        Assert.Equal("0", GetField<string>(cut.Instance, "keypadText"));

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "PressPaymentKey", "5"));
        Assert.Equal(5m, GetField<decimal>(cut.Instance, "tipAmount"));
        Assert.True(GetField<bool>(cut.Instance, "tipAmountManuallyEdited"));
    }

    [Fact]
    public async Task Checkout_returns_early_for_unpayable_state_or_closed_cash_register()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        CheckoutHandler handler = new(bill);
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill, handler);

        SetField(cut.Instance, "session", null!);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CheckoutSession"));
        Assert.Null(handler.CheckoutRequest);

        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(bill.Id, restaurantId, bill.TableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "bill", null!);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CheckoutSession"));
        Assert.Null(handler.CheckoutRequest);

        SetField(cut.Instance, "bill", bill with { Outstanding = 0m });
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CheckoutSession"));
        Assert.Null(handler.CheckoutRequest);

        SetField(cut.Instance, "bill", bill with { CanCheckout = false });
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CheckoutSession"));
        Assert.Null(handler.CheckoutRequest);

        SetField(cut.Instance, "bill", bill);
        SetField(cut.Instance, "busy", true);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CheckoutSession"));
        Assert.Null(handler.CheckoutRequest);

        SetField(cut.Instance, "busy", false);
        SetField(cut.Instance, "cashRegister", null!);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CheckoutSession"));
        Assert.Null(handler.CheckoutRequest);
        Assert.Contains(
            "Debes abrir la caja antes de registrar el cobro",
            GetField<string>(cut.Instance, "message")
        );

        SetField(
            cut.Instance,
            "cashRegister",
            new CashRegisterResponse(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.Today),
                "Open",
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                null,
                null,
                null,
                DateTime.UtcNow,
                "Cajero",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );
        SetField(cut.Instance, "paymentMethod", "Cash");
        SetField(cut.Instance, "currentChargeAmount", 0m);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CheckoutSession"));
        Assert.Null(handler.CheckoutRequest);
    }

    [Fact]
    public async Task Sending_order_requires_an_open_register_a_location_or_a_customer_name()
    {
        Guid restaurantId = Guid.NewGuid();
        TakeawayOrderHandler handler = new(
            new OrderResponse(
                Guid.NewGuid(),
                restaurantId,
                null,
                null,
                "Ana",
                "Takeaway",
                "OnAccount",
                "Submitted",
                12m,
                DateTime.UtcNow
            )
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "SendOrder"));
        Assert.Contains(
            "Debes abrir la caja antes de tomar pedidos",
            GetField<string>(cut.Instance, "message")
        );

        SetField(
            cut.Instance,
            "cashRegister",
            new CashRegisterResponse(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.Today),
                "Open",
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                null,
                null,
                null,
                DateTime.UtcNow,
                "Cajero",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );
        SetField(cut.Instance, "takeawayOpen", false);
        SetField(cut.Instance, "serviceMode", "DineIn");
        await cut.InvokeAsync(() => Invoke(cut.Instance, "SendOrder"));
        Assert.Null(handler.CreatedPayload);

        SetField(cut.Instance, "takeawayOpen", true);
        SetField(cut.Instance, "serviceMode", "Takeaway");
        SetField(cut.Instance, "customerName", "  ");
        await cut.InvokeAsync(() => Invoke(cut.Instance, "SendOrder"));
        Assert.Null(handler.CreatedPayload);
    }

    [Fact]
    public async Task Cancelling_a_new_guest_count_prompt_cancels_the_empty_session()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        OpenSessionCancellationHandler handler = new(restaurantId, tableId, sessionId);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "cashRegister",
            new CashRegisterResponse(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.Today),
                "Open",
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                null,
                null,
                null,
                DateTime.UtcNow,
                "Cajero",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );
        TableResponse table = new(
            tableId,
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Occupied",
            null,
            Guid.NewGuid(),
            "Sala",
            1
        );
        MethodInfo select = typeof(PosHome).GetMethod(
            "SelectTable",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(() => (Task)select.Invoke(cut.Instance, [table])!);
        Assert.True(GetField<bool>(cut.Instance, "guestCountDialogOpen"));

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CancelGuestCount"));

        Assert.Contains($"/api/dining/sessions/{sessionId}/cancel", handler.Paths);
        Assert.False(GetField<bool>(cut.Instance, "guestCountDialogOpen"));
        Assert.Null(GetField<DiningSessionResponse?>(cut.Instance, "session"));
        Assert.False(handler.MenuWasLoaded);
    }

    [Fact]
    public async Task Returning_from_a_new_empty_table_session_cancels_its_opening()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        OpenSessionCancellationHandler handler = new(restaurantId, tableId, sessionId);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "cashRegister", OpenRegister(restaurantId));
        TableResponse table = new(
            tableId,
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Occupied",
            null,
            Guid.NewGuid(),
            "Sala",
            1
        );
        MethodInfo select = typeof(PosHome).GetMethod(
            "SelectTable",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(() => (Task)select.Invoke(cut.Instance, [table])!);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "AcceptGuestCount"));

        await cut.InvokeAsync(() => Invoke(cut.Instance, "BackFromTableOrder"));

        Assert.Contains($"/api/dining/sessions/{sessionId}/cancel", handler.Paths);
        Assert.Null(GetField<DiningSessionResponse?>(cut.Instance, "session"));
        Assert.False(GetField<bool>(cut.Instance, "hasSessionOrders"));
    }

    [Fact]
    public async Task Showing_pickup_orders_clears_table_filters_and_closing_dialogs_respects_busy_state()
    {
        Guid restaurantId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(
            restaurantId,
            [],
            new OperationalRefreshHandler(restaurantId)
        );
        SetField(cut.Instance, "pickupView", false);
        SetField(cut.Instance, "selectedZoneId", Guid.NewGuid());
        SetField(cut.Instance, "tableFilter", "Occupied");
        await cut.InvokeAsync(() => Invoke(cut.Instance, "ShowPickupOrders"));
        Assert.True(GetField<bool>(cut.Instance, "pickupView"));
        Assert.Null(GetField<Guid?>(cut.Instance, "selectedZoneId"));
        Assert.Equal("All", GetField<string>(cut.Instance, "tableFilter"));

        SetField(cut.Instance, "billOpen", true);
        SetField(cut.Instance, "accountPreviewOpen", true);
        SetField(cut.Instance, "salesTicketOpen", true);
        SetField(cut.Instance, "paymentDialogOpen", true);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "CloseBill"));
        Assert.False(GetField<bool>(cut.Instance, "billOpen"));
        Assert.False(GetField<bool>(cut.Instance, "accountPreviewOpen"));
        Assert.False(GetField<bool>(cut.Instance, "salesTicketOpen"));
        Assert.False(GetField<bool>(cut.Instance, "paymentDialogOpen"));

        SetField(cut.Instance, "paymentDialogOpen", true);
        SetField(cut.Instance, "busy", true);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "ClosePaymentDialog"));
        Assert.True(GetField<bool>(cut.Instance, "paymentDialogOpen"));
        SetField(cut.Instance, "busy", false);
        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "ClosePaymentDialog"));
        Assert.False(GetField<bool>(cut.Instance, "paymentDialogOpen"));
    }

    [Fact]
    public async Task Realtime_refresh_ignores_events_without_a_matching_restaurant_session()
    {
        Guid restaurantId = Guid.NewGuid();
        OperationalRefreshHandler handler = new(restaurantId);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        MethodInfo schedule = typeof(PosHome).GetMethod(
            "ScheduleRealtimeRefresh",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        SetField(cut.Instance, "login", null!);
        Task withoutLogin = (Task)schedule.Invoke(cut.Instance, [restaurantId])!;
        await withoutLogin;

        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "cajero", "Cajero"),
                []
            )
        );
        OrderRealtimeNotification unrelatedOrder = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Ready",
            1,
            DateTime.UtcNow,
            []
        );
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "HandleOrderUpdated", unrelatedOrder));
        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "HandleDiningTableUpdated", Guid.NewGuid())
        );
        Assert.Null(GetField<CancellationTokenSource?>(cut.Instance, "realtimeRefresh"));

        await (Task)schedule.Invoke(cut.Instance, [Guid.NewGuid()])!;
        Assert.Null(GetField<CancellationTokenSource?>(cut.Instance, "realtimeRefresh"));

        OrderRealtimeNotification matchingOrder = unrelatedOrder with
        {
            RestaurantId = restaurantId,
        };
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "HandleOrderUpdated", matchingOrder));
        await Task.Delay(500, Xunit.TestContext.Current.CancellationToken);
        Assert.Contains(
            handler.Paths,
            path => path == $"/api/dining/restaurants/{restaurantId}/tables"
        );
        Assert.Contains(handler.Paths, path => path == "/api/orders/pos");
    }

    [Fact]
    public async Task Realtime_refresh_is_silent_when_cancelled_and_reports_failed_table_reads()
    {
        Guid restaurantId = Guid.NewGuid();
        OperationalRefreshHandler handler = new(restaurantId) { FailTables = true };
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        MethodInfo refresh = typeof(PosHome).GetMethod(
            "RefreshAfterOrderEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        string? initialMessage = GetField<string?>(cut.Instance, "message");

        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RefreshAfterOrderEventAsync", cancelled.Token)
        );

        Assert.Equal(initialMessage, GetField<string?>(cut.Instance, "message"));
        await cut.InvokeAsync(() => (Task)refresh.Invoke(cut.Instance, [CancellationToken.None])!);

        Assert.Contains(
            "No se pudo actualizar el TPV en tiempo real",
            GetField<string>(cut.Instance, "message")
        );
    }

    [Fact]
    public async Task Selecting_a_table_requests_guest_count_before_loading_the_menu()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        GuestCountHandler handler = new(restaurantId, tableId, sessionId);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        TableResponse table = new(
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
        );
        MethodInfo select = typeof(PosHome).GetMethod(
            "SelectTable",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)select.Invoke(cut.Instance, [table])!);
        await Rerender(cut);

        Assert.True(GetField<bool>(cut.Instance, "guestCountDialogOpen"));
        Assert.Null(GetField<TableResponse?>(cut.Instance, "selectedTable"));
        Assert.Contains("comensales", cut.Markup);

        MethodInfo accept = typeof(PosHome).GetMethod(
            "AcceptGuestCount",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(() => (Task)accept.Invoke(cut.Instance, null)!);
        await Rerender(cut);

        Assert.False(GetField<bool>(cut.Instance, "guestCountDialogOpen"));
        Assert.Equal(tableId, GetField<TableResponse?>(cut.Instance, "selectedTable")!.Id);
        Assert.Equal(2, handler.GuestCount);
        Assert.True(handler.MenuWasLoaded);
    }

    [Fact]
    public async Task Cancelling_guest_count_returns_to_tables_without_loading_the_menu()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        GuestCountHandler handler = new(restaurantId, tableId, sessionId);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        TableResponse table = new(
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
        );
        MethodInfo select = typeof(PosHome).GetMethod(
            "SelectTable",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(() => (Task)select.Invoke(cut.Instance, [table])!);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CancelGuestCount"));
        await Rerender(cut);

        Assert.False(GetField<bool>(cut.Instance, "guestCountDialogOpen"));
        Assert.Null(GetField<TableResponse?>(cut.Instance, "selectedTable"));
        Assert.False(handler.MenuWasLoaded);
    }

    [Fact]
    public async Task Loading_visible_table_orders_keeps_matching_sessions_and_marks_failed_reads()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();
        Guid currentSessionId = Guid.NewGuid();
        Guid staleSessionId = Guid.NewGuid();
        Guid currentTableId = Guid.NewGuid();
        Guid staleTableId = Guid.NewGuid();
        Guid failedTableId = Guid.NewGuid();
        Guid availableTableId = Guid.NewGuid();
        VisibleSessionHandler handler = new(
            restaurantId,
            currentTableId,
            currentSessionId,
            staleTableId,
            failedTableId
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "takeawayOpen", false);
        SetField(cut.Instance, "serviceMode", "DineIn");
        SetField(cut.Instance, "selectedZoneId", zoneId);
        SetField(
            cut.Instance,
            "tables",
            new List<TableResponse>
            {
                new(
                    currentTableId,
                    restaurantId,
                    "M1",
                    "Mesa 1",
                    true,
                    "Occupied",
                    currentSessionId,
                    zoneId,
                    "Sala",
                    4
                ),
                new(
                    staleTableId,
                    restaurantId,
                    "M2",
                    "Mesa 2",
                    true,
                    "Occupied",
                    staleSessionId,
                    zoneId,
                    "Sala",
                    4
                ),
                new(
                    failedTableId,
                    restaurantId,
                    "M3",
                    "Mesa 3",
                    true,
                    "Occupied",
                    Guid.NewGuid(),
                    zoneId,
                    "Sala",
                    4
                ),
                new(
                    availableTableId,
                    restaurantId,
                    "M4",
                    "Mesa 4",
                    true,
                    "Available",
                    null,
                    zoneId,
                    "Sala",
                    4
                ),
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "LoadVisibleTableSessionOrdersAsync"));

        cut.Render();
        Assert.Contains("Actualizando comandas", cut.Markup);
        IDictionary snapshots = GetField<IDictionary>(cut.Instance, "tableSessionOrderSnapshots");
        Assert.Equal(2, snapshots.Count);
        Assert.True(snapshots.Contains(currentTableId));
        Assert.True(snapshots.Contains(failedTableId));
        Assert.False(snapshots.Contains(staleTableId));
        Assert.False(snapshots.Contains(availableTableId));

        SetField(cut.Instance, "takeawayOpen", true);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "LoadVisibleTableSessionOrdersAsync"));
        Assert.Equal(2, snapshots.Count);
    }

    [Fact]
    public async Task Visible_table_session_reads_discard_results_after_zone_changes()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid originalZoneId = Guid.NewGuid();
        Guid activeSessionId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        DelayedVisibleSessionHandler handler = new(restaurantId, tableId, activeSessionId);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "takeawayOpen", false);
        SetField(cut.Instance, "serviceMode", "DineIn");
        SetField(cut.Instance, "selectedZoneId", originalZoneId);
        SetField(
            cut.Instance,
            "tables",
            new List<TableResponse>
            {
                new(
                    tableId,
                    restaurantId,
                    "M1",
                    "Mesa 1",
                    true,
                    "Occupied",
                    activeSessionId,
                    originalZoneId,
                    "Sala",
                    1
                ),
            }
        );

        Task load = cut.InvokeAsync(() =>
            Invoke(cut.Instance, "LoadVisibleTableSessionOrdersAsync")
        );
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            Xunit.TestContext.Current.CancellationToken
        );
        await cut.InvokeAsync(() => SetField(cut.Instance, "selectedZoneId", Guid.NewGuid()));
        handler.CompleteRequest();
        await load;

        Assert.Empty(GetField<IDictionary>(cut.Instance, "tableSessionOrderSnapshots"));
        Assert.Equal(1, handler.RequestCount);

        SetField(cut.Instance, "selectedZoneId", null!);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "LoadVisibleTableSessionOrdersAsync"));

        Assert.Empty(GetField<IDictionary>(cut.Instance, "tableSessionOrderSnapshots"));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Table_refresh_preserves_an_available_zone_and_resets_a_removed_zone_filter()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid selectedZoneId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        TableResponse table = new(
            Guid.NewGuid(),
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Occupied",
            Guid.NewGuid(),
            selectedZoneId,
            "Sala",
            1
        );
        SetField(cut.Instance, "selectedZoneId", selectedZoneId);
        SetField(cut.Instance, "tableFilter", "Occupied");

        await cut.InvokeAsync(() =>
            InvokeVoid(cut.Instance, "SetTables", new List<TableResponse> { table })
        );

        Assert.Equal(selectedZoneId, GetField<Guid?>(cut.Instance, "selectedZoneId"));
        Assert.Equal("Occupied", GetField<string>(cut.Instance, "tableFilter"));
        Assert.Equal(table, Assert.Single(GetField<List<TableResponse>>(cut.Instance, "tables")));

        await cut.InvokeAsync(() =>
            InvokeVoid(
                cut.Instance,
                "SetTables",
                new List<TableResponse> { table with { ZoneId = Guid.NewGuid() } }
            )
        );

        Assert.Null(GetField<Guid?>(cut.Instance, "selectedZoneId"));
        Assert.Equal("All", GetField<string>(cut.Instance, "tableFilter"));
    }

    [Fact]
    public async Task Table_summaries_and_kitchen_clocks_follow_active_order_state()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        TableResponse table = new(
            tableId,
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            Guid.NewGuid(),
            "Sala",
            2
        );
        DateTime submittedAt = DateTime.UtcNow.AddMinutes(-5);
        OrderResponse readyOrder = new(
            Guid.NewGuid(),
            restaurantId,
            tableId,
            sessionId,
            string.Empty,
            "DineIn",
            "OnAccount",
            "Ready",
            12m,
            submittedAt,
            submittedAt,
            [
                new OrderStationProgressResponse(
                    "KITCHEN",
                    "Cocina",
                    "Ready",
                    submittedAt.AddMinutes(2)
                ),
                new OrderStationProgressResponse("BAR", "Barra", "Cancelled"),
            ]
        );
        SetField(cut.Instance, "activeOrders", new List<OrderResponse> { readyOrder });

        MethodInfo summary = typeof(PosHome).GetMethod(
            "TableOrderSummary",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo sessionSummary = typeof(PosHome).GetMethod(
            "SessionOrderSummary",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo kitchenTime = typeof(PosHome).GetMethod(
            "TableKitchenTime",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.Equal("1 lista", summary.Invoke(cut.Instance, [table]));
        Assert.Equal("1 lista", sessionSummary.Invoke(cut.Instance, [sessionId]));
        Assert.Equal("02:00", kitchenTime.Invoke(cut.Instance, [table]));

        SetField(
            cut.Instance,
            "activeOrders",
            new List<OrderResponse>
            {
                readyOrder with
                {
                    Stations =
                    [
                        new OrderStationProgressResponse("KITCHEN", "Cocina", "Dispatched"),
                    ],
                },
            }
        );
        Assert.Null(kitchenTime.Invoke(cut.Instance, [table]));

        SetField(
            cut.Instance,
            "activeOrders",
            new List<OrderResponse>
            {
                readyOrder with
                {
                    SubmittedAtUtc = null,
                    Stations =
                    [
                        new OrderStationProgressResponse("KITCHEN", "Cocina", "InPreparation"),
                    ],
                },
            }
        );
        Assert.Null(kitchenTime.Invoke(cut.Instance, [table]));
        Assert.Equal(
            "Sin comandas activas",
            summary.Invoke(cut.Instance, [table with { ActiveSessionId = null }])
        );
    }

    [Fact]
    public async Task Opening_cash_register_closes_the_panel_without_opening_a_summary()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterResponse opened = new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Open",
            25m,
            0m,
            0m,
            0m,
            0m,
            25m,
            25m,
            null,
            null,
            null,
            DateTime.UtcNow,
            "Cajero",
            null,
            null,
            [],
            [],
            [],
            []
        );
        OpenCashRegisterHandler handler = new(opened);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(cut.Instance, "openingFloat", 25m);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenDailyCashRegister"));
        await Rerender(cut);

        CashRegisterResponse? current = GetField<CashRegisterResponse?>(
            cut.Instance,
            "cashRegister"
        );
        Assert.Equal(opened.Id, current?.Id);
        Assert.Equal(opened.OpeningFloat, current?.OpeningFloat);
        Assert.Equal("Open", current?.Status);
        Assert.False(GetField<bool>(cut.Instance, "cashPanelOpen"));
        Assert.False(GetField<bool>(cut.Instance, "cashCloseTicketOpen"));
        Assert.Contains(
            "Caja diaria abierta con 25,00 €",
            GetField<string>(cut.Instance, "message")
        );
        Assert.DoesNotContain("Arqueo cerrado", cut.Markup);
    }

    [Fact]
    public async Task Opening_cash_register_reports_api_failure_without_losing_the_float()
    {
        Guid restaurantId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(cut.Instance, "openingFloat", 25m);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenDailyCashRegister"));

        Assert.Equal(25m, GetField<decimal>(cut.Instance, "openingFloat"));
        Assert.True(GetField<bool>(cut.Instance, "cashPanelOpen"));
        Assert.False(GetField<bool>(cut.Instance, "busy"));
        Assert.False(GetField<bool>(cut.Instance, "success"));
        Assert.Contains("API 404", GetField<string>(cut.Instance, "message"));
    }

    [Fact]
    public async Task Takeaway_requires_an_open_register_and_starts_with_a_fresh_menu_and_cart()
    {
        Guid restaurantId = Guid.NewGuid();
        MenuItemResponse product = new(
            restaurantId,
            Guid.NewGuid(),
            "COFFEE",
            "Café",
            Guid.NewGuid(),
            "DRINK",
            "Bebidas",
            2m,
            true,
            "BAR",
            "Bar"
        );
        OperationalRefreshHandler handler = new(restaurantId);
        handler.Menu.Add(product);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "takeawayOpen", false);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "BeginTakeaway"));

        Assert.False(GetField<bool>(cut.Instance, "takeawayOpen"));
        Assert.Equal(0, handler.MenuReads);
        Assert.Contains("Debes abrir la caja", GetField<string>(cut.Instance, "message"));

        SetField(
            cut.Instance,
            "cashRegister",
            new CashRegisterResponse(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.Today),
                "Open",
                25m,
                0m,
                0m,
                0m,
                0m,
                25m,
                25m,
                null,
                null,
                null,
                DateTime.UtcNow,
                "Cajero",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );
        SetField(cut.Instance, "customerName", "Cliente anterior");
        SetField(cut.Instance, "pickupView", true);
        SetField(cut.Instance, "menuRefreshVersion", 2);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "BeginTakeaway"));

        Assert.True(GetField<bool>(cut.Instance, "takeawayOpen"));
        Assert.False(GetField<bool>(cut.Instance, "pickupView"));
        Assert.Equal("Takeaway", GetField<string>(cut.Instance, "serviceMode"));
        Assert.Equal(string.Empty, GetField<string>(cut.Instance, "customerName"));
        Assert.Equal(
            product.ProductId,
            Assert.Single(GetField<List<MenuItemResponse>>(cut.Instance, "menu")).ProductId
        );
        Assert.Empty(GetField<List<PosCartLine>>(cut.Instance, "cart"));
        Assert.Equal(3, GetField<int>(cut.Instance, "menuRefreshVersion"));
        Assert.Equal(1, handler.MenuReads);
    }

    [Fact]
    public async Task Opening_float_keypad_limits_precision_and_resets_the_selected_amount()
    {
        IRenderedComponent<PosHome> cut = await RenderTakeaway(Guid.NewGuid(), []);

        await cut.InvokeAsync(() =>
        {
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", "1");
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", ",");
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", ",");
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", "2");
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", "3");
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", "4");
        });

        Assert.Equal(1.23m, GetField<decimal>(cut.Instance, "openingFloat"));
        Assert.Equal("1,23", GetField<string>(cut.Instance, "openingFloatKeypadText"));

        await cut.InvokeAsync(() =>
        {
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", "Borrar");
            InvokeVoid(cut.Instance, "PressOpeningFloatKey", "9");
        });

        Assert.Equal(9m, GetField<decimal>(cut.Instance, "openingFloat"));
        Assert.Equal("9", GetField<string>(cut.Instance, "openingFloatKeypadText"));

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "SelectCashMovementType", "Income"));
        Assert.Equal("Income", GetField<string>(cut.Instance, "cashMovementType"));
        Assert.Equal("Aportación de caja", GetField<string>(cut.Instance, "cashMovementCategory"));
        await cut.InvokeAsync(() =>
            InvokeVoid(cut.Instance, "SelectCashMovementType", "unexpected")
        );
        Assert.Equal("Expense", GetField<string>(cut.Instance, "cashMovementType"));
        Assert.Equal("Pago a proveedor", GetField<string>(cut.Instance, "cashMovementCategory"));
    }

    [Fact]
    public async Task Menu_updates_refresh_the_open_takeaway_only_for_its_restaurant()
    {
        Guid restaurantId = Guid.NewGuid();
        OperationalRefreshHandler handler = new(restaurantId);
        handler.Menu.Add(
            new MenuItemResponse(
                restaurantId,
                Guid.NewGuid(),
                "COFFEE",
                "Café",
                Guid.NewGuid(),
                "DRINKS",
                "Bebidas",
                2m,
                true,
                "BAR",
                "Bar",
                PhotoSmallUrl: "/media/cafe-small.jpg"
            )
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);

        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "HandleMenuChanged", Guid.NewGuid()));
        Assert.Empty(GetField<List<MenuItemResponse>>(cut.Instance, "menu"));
        Assert.Equal(0, handler.MenuReads);

        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "HandleMenuChanged", restaurantId));
        Assert.Equal(
            "Café",
            Assert.Single(GetField<List<MenuItemResponse>>(cut.Instance, "menu")).ProductName
        );
        Assert.Equal("/media/cafe-small.jpg", cut.Find(".product-tile img").GetAttribute("src"));
        Assert.Equal(1, handler.MenuReads);

        SetField(cut.Instance, "takeawayOpen", false);
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "HandleMenuChanged", restaurantId));
        Assert.Equal(1, handler.MenuReads);

        SetField(cut.Instance, "takeawayOpen", true);
        handler.FailMenu = true;
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "HandleMenuChanged", restaurantId));
        Assert.Contains(
            "No se pudo actualizar la carta",
            GetField<string>(cut.Instance, "message")
        );
    }

    [Fact]
    public async Task Realtime_refresh_keeps_the_active_table_and_clears_a_session_removed_by_the_server()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        TableResponse occupied = new(
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
        );
        OperationalRefreshHandler handler = new(restaurantId)
        {
            Session = new DiningSessionResponse(
                sessionId,
                restaurantId,
                tableId,
                "Pos",
                "Open",
                []
            ),
        };
        handler.Tables.Add(occupied);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "takeawayOpen", false);
        SetField(cut.Instance, "selectedTable", occupied);
        SetField(cut.Instance, "session", handler.Session);

        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RefreshAfterOrderEventAsync", CancellationToken.None)
        );
        Assert.Equal(sessionId, GetField<DiningSessionResponse>(cut.Instance, "session").Id);
        Assert.Equal(tableId, GetField<TableResponse>(cut.Instance, "selectedTable").Id);
        Assert.Equal(1, handler.ActiveSessionReads);

        handler.Tables.Clear();
        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RefreshAfterOrderEventAsync", CancellationToken.None)
        );
        Assert.Null(GetField<DiningSessionResponse?>(cut.Instance, "session"));
        Assert.Null(GetField<TableResponse?>(cut.Instance, "selectedTable"));
    }

    [Fact]
    public async Task Pickup_detail_distinguishes_unconfirmed_payment_from_paid_and_ready_orders()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        OrderResponse pickup = new(
            orderId,
            restaurantId,
            null,
            null,
            "Ana",
            "Takeaway",
            "OnAccount",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        OrderLineDetailResponse cancelled = new(
            Guid.NewGuid(),
            "Bocadillo",
            12m,
            1,
            12m,
            "KITCHEN",
            "Cocina",
            "Cancelled",
            DateTime.UtcNow,
            "Sin existencias",
            "Pending"
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        SetField(cut.Instance, "pickupBillOpen", true);
        SetField(cut.Instance, "selectedPickup", pickup);
        SetField(
            cut.Instance,
            "pickupDetail",
            new OrderDetailResponse(orderId, restaurantId, "Submitted", 12m, [cancelled], [])
        );

        SetField(cut.Instance, "pickupPaymentStatus", "Open");
        SetField(cut.Instance, "paymentMethod", "Cash");
        cut.Render();
        Assert.Contains("Abre la caja diaria antes de cobrar este pedido", cut.Markup);
        Assert.Contains("Cancelado · Sin existencias", cut.Markup);

        SetField(cut.Instance, "pickupPaymentStatus", "ProjectionPending");
        await Rerender(cut);
        Assert.Contains("El estado del pago aún no está disponible", cut.Markup);

        SetField(cut.Instance, "pickupPaymentStatus", "Paid");
        await Rerender(cut);
        Assert.Contains("Se podrá entregar cuando cocina lo marque listo", cut.Markup);
        Assert.DoesNotContain("Entregar al cliente", cut.Markup);

        SetField(
            cut.Instance,
            "pickupDetail",
            new OrderDetailResponse(orderId, restaurantId, "Ready", 12m, [cancelled], [])
        );
        await Rerender(cut);
        Assert.Contains("Entregar al cliente", cut.Markup);
    }

    [Fact]
    public async Task Refreshing_pickup_orders_updates_the_open_pickup_payment_projection()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        OrderResponse pickup = new(
            orderId,
            restaurantId,
            null,
            null,
            "Ana",
            "Takeaway",
            "OnAccount",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        OrderLineDetailResponse line = new(
            Guid.NewGuid(),
            "Bocadillo",
            12m,
            1,
            12m,
            "COCINA",
            "Cocina",
            "Active",
            null,
            "",
            KitchenStatus: "Pending"
        );
        PickupLifecycleHandler handler = new(restaurantId, pickup, line);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "pickupBillOpen", true);
        SetField(cut.Instance, "selectedPickup", pickup);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "LoadPickupOrdersCore"));

        Assert.Equal(
            orderId,
            Assert.Single(GetField<List<OrderResponse>>(cut.Instance, "pickupOrders")).Id
        );
        Assert.Equal("Open", GetField<string>(cut.Instance, "pickupPaymentStatus"));
        Assert.Equal(
            "Open",
            GetField<Dictionary<Guid, string>>(cut.Instance, "pickupPaymentStatuses")[orderId]
        );
        Assert.Equal("Submitted", GetField<OrderResponse>(cut.Instance, "selectedPickup").Status);
        Assert.IsType<OrderDetailResponse>(
            GetField<OrderDetailResponse?>(cut.Instance, "pickupDetail")
        );
        Assert.Equal(
            2,
            handler.Paths.Count(path =>
                path.StartsWith("/api/payments/restaurants/", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task Pickup_can_be_opened_paid_and_delivered_from_its_detail()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        OrderResponse pickup = new(
            orderId,
            restaurantId,
            null,
            null,
            "Ana",
            "Takeaway",
            "OnAccount",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        OrderLineDetailResponse line = new(
            lineId,
            "Bocadillo",
            12m,
            1,
            12m,
            "KITCHEN",
            "Cocina",
            "Active",
            null,
            string.Empty,
            "Pending"
        );
        PickupLifecycleHandler handler = new(restaurantId, pickup, line);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(
            restaurantId,
            [],
            handler,
            new PickupJsRuntime()
        );
        MethodInfo openPickup = typeof(PosHome).GetMethod(
            "OpenPickup",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(() => (Task)openPickup.Invoke(cut.Instance, [pickup])!);

        Assert.True(GetField<bool>(cut.Instance, "pickupBillOpen"));
        Assert.Equal("Open", GetField<string>(cut.Instance, "pickupPaymentStatus"));
        Assert.Equal(12m, GetField<OrderResponse>(cut.Instance, "selectedPickup").Total);
        MethodInfo canCancel = typeof(PosHome).GetMethod(
            "CanCancelPickupLine",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.True((bool)canCancel.Invoke(cut.Instance, [line])!);

        MethodInfo cancelPickupLine = typeof(PosHome).GetMethod(
            "CancelPickupLine",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(() => (Task)cancelPickupLine.Invoke(cut.Instance, [line])!);
        Assert.Contains("Bocadillo cancelado", GetField<string>(cut.Instance, "message"));
        Assert.False(
            (bool)
                canCancel.Invoke(
                    cut.Instance,
                    [GetField<OrderDetailResponse>(cut.Instance, "pickupDetail").Lines.Single()]
                )!
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CapturePickup"));
        Assert.Equal("Open", GetField<string>(cut.Instance, "pickupPaymentStatus"));
        Assert.Contains("Debes abrir la caja", GetField<string>(cut.Instance, "message"));

        SetField(
            cut.Instance,
            "cashRegister",
            new CashRegisterResponse(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.Today),
                "Open",
                25m,
                0m,
                0m,
                0m,
                0m,
                25m,
                25m,
                null,
                null,
                null,
                DateTime.UtcNow,
                "Cajero",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CapturePickup"));
        Assert.Equal("Paid", GetField<string>(cut.Instance, "pickupPaymentStatus"));
        Assert.True(GetField<bool>(cut.Instance, "success"));
        int captureRequests = handler.Paths.Count(path =>
            path.EndsWith("/capture", StringComparison.Ordinal)
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "CapturePickup"));
        Assert.Equal(
            captureRequests,
            handler.Paths.Count(path => path.EndsWith("/capture", StringComparison.Ordinal))
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "DeliverPickup"));
        Assert.False(GetField<bool>(cut.Instance, "pickupBillOpen"));
        Assert.Null(GetField<OrderResponse?>(cut.Instance, "selectedPickup"));
        Assert.Contains("Pedido entregado", GetField<string>(cut.Instance, "message"));
        Assert.Contains(handler.Paths, path => path.EndsWith("/capture", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, path => path.EndsWith("/deliver", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelling_a_pickup_line_stops_when_the_reason_is_blank_or_confirmation_is_denied()
    {
        Guid restaurantId = Guid.NewGuid();
        OrderResponse pickup = new(
            Guid.NewGuid(),
            restaurantId,
            null,
            null,
            "Ana",
            "Takeaway",
            "OnAccount",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        OrderLineDetailResponse line = new(
            Guid.NewGuid(),
            "Bocadillo",
            12m,
            1,
            12m,
            "KITCHEN",
            "Cocina",
            "Active",
            null,
            string.Empty,
            "Pending"
        );
        PickupLifecycleHandler handler = new(restaurantId, pickup, line);
        PickupJsRuntime js = new() { PromptResult = "   " };
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler, js);
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "CancelPickupLine", line));
        Assert.Equal(0, js.ConfirmationCount);
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "OpenPickup", pickup));

        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "CancelPickupLine", line));
        Assert.DoesNotContain(
            handler.Paths,
            path => path.EndsWith("/cancel", StringComparison.Ordinal)
        );
        Assert.Equal(0, js.ConfirmationCount);

        js.PromptResult = "Cliente cambió de opinión";
        js.Confirmation = false;
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "CancelPickupLine", line));

        Assert.Equal(1, js.ConfirmationCount);
        Assert.DoesNotContain(
            handler.Paths,
            path => path.EndsWith("/cancel", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Sending_takeaway_submits_customer_and_cart_then_refreshes_pickup_orders()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        OrderResponse submitted = new(
            orderId,
            restaurantId,
            null,
            null,
            "Ana",
            "Takeaway",
            "OnAccount",
            "Submitted",
            12m,
            DateTime.UtcNow,
            DateTime.UtcNow
        );
        TakeawayOrderHandler handler = new(submitted);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "cashRegister",
            new CashRegisterResponse(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.Today),
                "Open",
                25m,
                0m,
                0m,
                0m,
                0m,
                25m,
                25m,
                null,
                null,
                null,
                DateTime.UtcNow,
                "Cajero",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );
        SetField(cut.Instance, "customerName", "  Ana  ");
        SetField(
            cut.Instance,
            "cart",
            new List<PosCartLine>
            {
                new()
                {
                    ProductId = Guid.NewGuid(),
                    Format = "PLATO",
                    BasePrice = 12m,
                    ExpectedUnitPrice = 12m,
                },
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "SendOrder"));

        Assert.Equal("Ana", handler.CreatedPayload!.Value.GetProperty("customerName").GetString());
        Assert.Equal(
            "Takeaway",
            handler.CreatedPayload.Value.GetProperty("serviceMode").GetString()
        );
        Assert.Equal(
            JsonValueKind.Null,
            handler.CreatedPayload.Value.GetProperty("tableId").ValueKind
        );
        Assert.Empty(GetField<List<PosCartLine>>(cut.Instance, "cart"));
        Assert.Equal(
            orderId,
            Assert.Single(GetField<List<OrderResponse>>(cut.Instance, "pickupOrders")).Id
        );
        Assert.False(GetField<bool>(cut.Instance, "takeawayOpen"));
    }

    [Fact]
    public async Task Confirming_cash_register_close_opens_the_close_ticket()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterResponse closed = new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Closed",
            25m,
            0m,
            0m,
            0m,
            0m,
            25m,
            25m,
            25m,
            25m,
            0m,
            DateTime.UtcNow.AddMinutes(-1),
            "Cajero",
            DateTime.UtcNow,
            "Cajero",
            [],
            [],
            [],
            []
        );
        CloseCashRegisterHandler handler = new(closed);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(
            restaurantId,
            [],
            handler,
            new ConfirmJsRuntime()
        );
        SetField(cut.Instance, "cashRegister", closed with { Status = "Open", ClosedAtUtc = null });
        SetField(cut.Instance, "cashPanelOpen", true);
        SetField(
            cut.Instance,
            "reconciliationAmounts",
            new Dictionary<string, decimal> { ["Cash"] = 25m }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CloseDailyCashRegister"));
        await Rerender(cut);

        Assert.True(GetField<bool>(cut.Instance, "cashCloseTicketOpen"));
        Assert.False(GetField<bool>(cut.Instance, "cashPanelOpen"));
        Assert.Contains("Arqueo cerrado", cut.Markup);
        Assert.Contains("Imprimir cierre de caja", cut.Markup);
    }

    [Fact]
    public async Task Declining_cash_register_close_keeps_the_open_register_unchanged()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterResponse opened = new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Open",
            25m,
            0m,
            0m,
            0m,
            0m,
            25m,
            25m,
            null,
            null,
            null,
            DateTime.UtcNow,
            "Cajero",
            null,
            null,
            [],
            [],
            [],
            []
        );
        ConfirmJsRuntime js = new(false);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], jsRuntime: js);
        SetField(cut.Instance, "cashRegister", opened);
        SetField(cut.Instance, "cashPanelOpen", true);
        MethodInfo close = typeof(PosHome).GetMethod(
            "CloseDailyCashRegister",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)close.Invoke(cut.Instance, null)!);
        await Rerender(cut);

        Assert.Equal(1, js.ConfirmationCount);
        Assert.Equal("Open", GetField<CashRegisterResponse>(cut.Instance, "cashRegister").Status);
        Assert.True(GetField<bool>(cut.Instance, "cashPanelOpen"));
        Assert.False(GetField<bool>(cut.Instance, "cashCloseTicketOpen"));

        SetField(cut.Instance, "cashRegister", null);
        await cut.InvokeAsync(() => (Task)close.Invoke(cut.Instance, null)!);
        Assert.Equal(1, js.ConfirmationCount);
    }

    [Fact]
    public async Task Creating_cash_income_uses_selected_category_and_resets_the_form()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        CashRegisterResponse updated = new(
            sessionId,
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Open",
            25m,
            0m,
            0m,
            5m,
            0m,
            25m,
            30m,
            null,
            null,
            null,
            DateTime.UtcNow,
            "Cajero",
            null,
            null,
            [],
            [],
            [],
            [
                new CashMovementResponse(
                    Guid.NewGuid(),
                    "Income",
                    "Cash",
                    5m,
                    "Reposición",
                    DateTime.UtcNow,
                    "Aportación de caja",
                    "Fondo"
                ),
            ]
        );
        CashMovementHandler handler = new(updated);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "cashRegister", updated);
        SetField(cut.Instance, "cashMovementType", "Income");
        SetField(cut.Instance, "cashMovementCategory", "Aportación de caja");
        SetField(cut.Instance, "cashMovementDescription", "Fondo");
        SetField(cut.Instance, "cashMovementAmount", 5m);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "CreateCashMovement"));

        Assert.Equal("Income", handler.Type);
        Assert.Equal("Cash", handler.Method);
        Assert.Equal(5m, handler.Amount);
        Assert.Equal("Aportación de caja", handler.Category);
        Assert.Equal("Fondo", handler.Description);
        Assert.Equal(
            30m,
            GetField<CashRegisterResponse?>(cut.Instance, "cashRegister")?.ExpectedTotal
        );
        Assert.Equal(string.Empty, GetField<string>(cut.Instance, "cashMovementDescription"));
        Assert.Equal(0m, GetField<decimal>(cut.Instance, "cashMovementAmount"));
        Assert.Contains("Ingreso de 5,00 € registrado", GetField<string>(cut.Instance, "message"));
    }

    [Fact]
    public async Task Paid_session_opens_sale_ticket_with_company_tax_and_payment_details()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId) with
        {
            Total = 54m,
            Outstanding = 0m,
            CanCheckout = false,
            PaidAtUtc = DateTime.UtcNow,
            GuestCount = 2,
            TipTotal = 3m,
            DiscountPercentage = 10m,
            DiscountAmount = 6m,
            Orders = [new(orderId, "Delivered", "Pending", 54m, 0m, DateTime.UtcNow, 3m)],
        };
        SaleTicketHandler handler = new(
            new SaleResponse([new SalePaymentBreakdown("Card", 1, 57m)], "Promoción", 10m, 6m)
        );
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill, handler);
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new(
                    restaurantId,
                    "CENTRO",
                    "Centro",
                    "Calle Mayor",
                    true,
                    new RestaurantCompanyResponse(
                        Guid.NewGuid(),
                        "Restaurantes SL",
                        "B12345678",
                        "Calle Fiscal 2",
                        "910000000",
                        "caja@example.test"
                    )
                ),
            }
        );
        SetField(
            cut.Instance,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [orderId] = new(
                    orderId,
                    restaurantId,
                    "Delivered",
                    60m,
                    [
                        new(
                            Guid.NewGuid(),
                            "Tortilla",
                            20m,
                            2,
                            50m,
                            "KITCHEN",
                            "Cocina",
                            "Active",
                            null,
                            string.Empty,
                            Format: "PLATO",
                            BaseUnitPrice: 20m,
                            Extras: [new(Guid.NewGuid(), "Queso", 5m, 1, 21m)],
                            TaxRate: 10m
                        ),
                        new(
                            Guid.NewGuid(),
                            "Cancelado",
                            10m,
                            1,
                            10m,
                            "KITCHEN",
                            "Cocina",
                            "Cancelled",
                            DateTime.UtcNow,
                            "Anulado",
                            TaxRate: 21m
                        ),
                    ],
                    []
                ),
            }
        );

        await Rerender(cut);
        Assert.Contains("Incluye 3,00 € de propina", cut.Markup);
        Assert.Contains("Preparando ticket de venta…", cut.Markup);
        Assert.DoesNotContain("aria-label=\"Ver ticket de venta\"", cut.Markup);

        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "RefreshSalesTicketAsync", bill.Id));
        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenSalesTicket"));
        await Rerender(cut);

        Assert.True(GetField<bool>(cut.Instance, "salesTicketOpen"));
        Assert.Contains("TICKET DE VENTA", cut.Markup);
        Assert.Contains("Restaurantes SL", cut.Markup);
        Assert.Contains("B12345678", cut.Markup);
        Assert.Contains("caja@example.test", cut.Markup);
        Assert.Contains("Ticket:", cut.Markup);
        Assert.Contains("Mesa 1", cut.Markup);
        Assert.Contains("Comensales: 2", cut.Markup);
        Assert.Contains("2 × Tortilla", cut.Markup);
        Assert.Contains("2 × Queso", cut.Markup);
        Assert.DoesNotContain("Cancelado", cut.Find(".sales-ticket").TextContent);
        Assert.Contains("Descuento (10 %)", cut.Markup);
        Assert.Contains("Total por comensal (2)", cut.Markup);
        Assert.Contains("Medio de pago · Tarjeta", cut.Markup);
        Assert.Contains("Propina", cut.Markup);

        await cut.Find("button[aria-label='Cerrar ticket de venta']").ClickAsync();
        Assert.False(GetField<bool>(cut.Instance, "salesTicketOpen"));
        Assert.NotNull(GetField<SaleResponse?>(cut.Instance, "salesTicket"));
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new(restaurantId, "CENTRO", "Centro", "Calle Mayor 8", true),
            }
        );
        SetField(
            cut.Instance,
            "bill",
            bill with
            {
                GuestCount = null,
                TipTotal = 0m,
                DiscountPercentage = 0m,
                DiscountAmount = 0m,
                PaymentMethod = "Mixed",
                PaidAtUtc = null,
            }
        );
        SetField(cut.Instance, "salesTicket", new SaleResponse());
        SetField(cut.Instance, "salesTicketOpen", true);
        await Rerender(cut);

        string ticketText = cut.Find(".sales-ticket").TextContent;
        Assert.Contains("Calle Mayor 8", ticketText);
        Assert.Contains("Sin desglose disponible", ticketText);
        Assert.DoesNotContain("Restaurantes SL", ticketText);
        Assert.DoesNotContain("Descuento (", ticketText);
        Assert.DoesNotContain("Total por comensal", ticketText);
        Assert.DoesNotContain("Propina", ticketText);
    }

    [Fact]
    public async Task Paid_session_offers_ticket_only_after_sales_completes_it()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse paidBill = Bill(restaurantId) with
        {
            Outstanding = 0m,
            CanCheckout = false,
            Orders = [new(Guid.NewGuid(), "Delivered", "Paid", 60m, 60m, DateTime.UtcNow)],
        };
        SaleTicketHandler handler = new(new SaleResponse()) { Ready = false };
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, paidBill, handler);

        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RefreshSalesTicketAsync", paidBill.Id)
        );
        await Rerender(cut);

        Assert.DoesNotContain("aria-label=\"Ver ticket de venta\"", cut.Markup);
        Assert.Contains("Preparando ticket de venta", cut.Markup);
        Assert.DoesNotContain("API 404", cut.Markup);

        handler.Ready = true;
        await cut.InvokeAsync(() => InvokeTask(cut.Instance, "HandleSaleCompleted", paidBill.Id));
        await Rerender(cut);

        Assert.Contains("aria-label=\"Ver ticket de venta\"", cut.Markup);
        await cut.Find("button[aria-label='Ver ticket de venta']").ClickAsync();
        Assert.True(GetField<bool>(cut.Instance, "salesTicketOpen"));
    }

    [Fact]
    public async Task Ticket_lookup_error_keeps_ticket_unavailable_and_allows_a_successful_retry()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse paidBill = Bill(restaurantId) with
        {
            Outstanding = 0m,
            CanCheckout = false,
            Orders = [new(Guid.NewGuid(), "Delivered", "Paid", 60m, 60m, DateTime.UtcNow)],
        };
        SaleTicketHandler handler = new(new SaleResponse())
        {
            FailureStatus = HttpStatusCode.ServiceUnavailable,
        };
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, paidBill, handler);

        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RefreshSalesTicketAsync", paidBill.Id)
        );
        await Rerender(cut);

        Assert.Null(GetField<Guid?>(cut.Instance, "readyTicketSessionId"));
        Assert.Null(GetField<SaleResponse?>(cut.Instance, "salesTicket"));
        Assert.False(
            string.IsNullOrWhiteSpace(GetField<string>(cut.Instance, "ticketAvailabilityError"))
        );
        Assert.Contains("No se pudo comprobar el ticket", cut.Markup);
        Assert.DoesNotContain("aria-label=\"Ver ticket de venta\"", cut.Markup);

        handler.FailureStatus = null;
        await cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RefreshSalesTicketAsync", paidBill.Id)
        );
        await Rerender(cut);

        Assert.Equal(paidBill.Id, GetField<Guid?>(cut.Instance, "readyTicketSessionId"));
        Assert.Null(GetField<string?>(cut.Instance, "ticketAvailabilityError"));
        Assert.Contains("aria-label=\"Ver ticket de venta\"", cut.Markup);
    }

    [Fact]
    public async Task Ticket_result_for_a_previous_session_is_ignored_after_the_selected_session_changes()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse paidBill = Bill(restaurantId) with
        {
            Outstanding = 0m,
            CanCheckout = false,
            Orders = [new(Guid.NewGuid(), "Delivered", "Paid", 60m, 60m, DateTime.UtcNow)],
        };
        DeferredSaleTicketHandler handler = new(new SaleResponse());
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, paidBill, handler);
        Task refresh = cut.InvokeAsync(() =>
            InvokeTask(cut.Instance, "RefreshSalesTicketAsync", paidBill.Id)
        );

        await handler.SaleRequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            Xunit.TestContext.Current.CancellationToken
        );
        await cut.InvokeAsync(() =>
            SetField(
                cut.Instance,
                "session",
                new DiningSessionResponse(
                    Guid.NewGuid(),
                    restaurantId,
                    paidBill.TableId,
                    "Pos",
                    "Open",
                    []
                )
            )
        );
        handler.CompleteSaleRequest();
        await refresh;

        Assert.Null(GetField<Guid?>(cut.Instance, "readyTicketSessionId"));
        Assert.Null(GetField<SaleResponse?>(cut.Instance, "salesTicket"));
    }

    [Fact]
    public async Task Ticket_cannot_open_until_the_bill_is_paid_and_its_sale_is_ready()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId) with { Outstanding = 1m, CanCheckout = true };
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill);
        SaleResponse ticket = new();

        SetField(cut.Instance, "salesTicket", ticket);
        SetField(cut.Instance, "readyTicketSessionId", bill.Id);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenSalesTicket"));
        Assert.False(GetField<bool>(cut.Instance, "salesTicketOpen"));

        SetField(
            cut.Instance,
            "bill",
            bill with
            {
                Outstanding = 0m,
                Total = 0m,
                CanCheckout = false,
            }
        );
        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenSalesTicket"));
        Assert.False(GetField<bool>(cut.Instance, "salesTicketOpen"));

        SetField(cut.Instance, "bill", bill with { Outstanding = 0m, CanCheckout = false });
        SetField(cut.Instance, "readyTicketSessionId", Guid.NewGuid());
        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenSalesTicket"));
        Assert.False(GetField<bool>(cut.Instance, "salesTicketOpen"));

        SetField(cut.Instance, "readyTicketSessionId", bill.Id);
        SetField(cut.Instance, "salesTicket", null);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenSalesTicket"));
        Assert.False(GetField<bool>(cut.Instance, "salesTicketOpen"));

        SetField(cut.Instance, "salesTicket", ticket);
        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenSalesTicket"));
        Assert.True(GetField<bool>(cut.Instance, "salesTicketOpen"));
    }

    [Fact]
    public async Task Checkout_allocates_partial_payment_only_to_active_unpaid_orders()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        CheckoutHandler handler = new(bill with { Outstanding = 30m });
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill, handler);

        cut.Find(".session-bill-card .bill-actions button.primary").Click();
        cut.FindAll(".payment-methods button")
            .Single(button => button.TextContent == "Tarjeta")
            .Click();
        cut.Find(".payment-keypad button[aria-label='3']").Click();
        cut.Find(".payment-keypad button[aria-label='0']").Click();
        cut.Find(".payment-dialog .bill-actions button.primary").Click();

        JsonElement request = Assert.IsType<JsonElement>(handler.CheckoutRequest);
        Assert.Equal("Card", request.GetProperty("method").GetString());
        Assert.Equal(30m, request.GetProperty("amountReceived").GetDecimal());
        Assert.Equal(0m, request.GetProperty("tipAmount").GetDecimal());
        JsonElement[] allocatedOrders = request
            .GetProperty("allocations")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, allocatedOrders.Length);
        Assert.Equal(bill.Orders[0].OrderId, allocatedOrders[0].GetProperty("orderId").GetGuid());
        Assert.Equal(25m, allocatedOrders[0].GetProperty("amount").GetDecimal());
        Assert.Equal(bill.Orders[1].OrderId, allocatedOrders[1].GetProperty("orderId").GetGuid());
        Assert.Equal(5m, allocatedOrders[1].GetProperty("amount").GetDecimal());
        Assert.Empty(cut.FindAll(".payment-dialog"));
        Assert.Contains("Pendiente de la cuenta: 30,00 €", cut.Markup);
    }

    [Fact]
    public async Task Full_cash_checkout_saves_tip_and_change_and_waits_for_ticket_projection()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        CheckoutHandler handler = new(
            bill with
            {
                Outstanding = 0m,
                CanCheckout = false,
                Orders = bill
                    .Orders.Select(order =>
                        order with
                        {
                            PaymentStatus = "Paid",
                            AmountPaid = order.Amount,
                        }
                    )
                    .ToList(),
            }
        );
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill, handler);

        cut.Find(".session-bill-card .bill-actions button.primary").Click();
        cut.FindAll(".payment-methods button")
            .Single(button => button.TextContent == "Efectivo")
            .Click();
        cut.Find(".payment-exact").Click();
        cut.FindAll(".tip-options button").Single(button => button.TextContent == "10%").Click();
        cut.Find(".payment-keypad button[aria-label='7']").Click();
        cut.Find(".payment-keypad button[aria-label='0']").Click();
        cut.Find(".payment-dialog .bill-actions button.primary").Click();

        JsonElement request = Assert.IsType<JsonElement>(handler.CheckoutRequest);
        Assert.Equal("Cash", request.GetProperty("method").GetString());
        Assert.Equal(76m, request.GetProperty("amountReceived").GetDecimal());
        Assert.Equal(6m, request.GetProperty("tipAmount").GetDecimal());
        Assert.Equal(10m, GetField<decimal>(cut.Instance, "lastChangeAmount"));
        Assert.Contains("Cobro de 76,00 € registrado", GetField<string>(cut.Instance, "message"));
        Assert.DoesNotContain("aria-label=\"Ver ticket de venta\"", cut.Markup);
    }

    [Fact]
    public async Task Account_preview_shows_extras_discount_and_tax_breakdown()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId) with
        {
            Total = 54m,
            Outstanding = 54m,
            DiscountPercentage = 10m,
            DiscountAmount = 6m,
            GuestCount = 2,
            Orders = [new(orderId, "Delivered", "Pending", 54m, 0m, DateTime.UtcNow)],
        };
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill);
        SetField(
            cut.Instance,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [orderId] = new(
                    orderId,
                    restaurantId,
                    "Delivered",
                    60m,
                    [
                        new(
                            Guid.NewGuid(),
                            "Tortilla",
                            20m,
                            2,
                            50m,
                            "KITCHEN",
                            "Cocina",
                            "Active",
                            null,
                            string.Empty,
                            Format: "PLATO",
                            BaseUnitPrice: 20m,
                            Extras: [new(Guid.NewGuid(), "Queso", 5m, 1, 21m)],
                            TaxRate: 10m
                        ),
                        new(
                            Guid.NewGuid(),
                            "Pan",
                            10m,
                            1,
                            10m,
                            "KITCHEN",
                            "Cocina",
                            "Active",
                            null,
                            string.Empty,
                            TaxRate: 10m
                        ),
                    ],
                    []
                ),
            }
        );
        await Rerender(cut);

        cut.FindAll(".session-bill-card .bill-actions button")
            .Single(button => button.TextContent == "Ver cuenta")
            .Click();

        Assert.Contains("2 × Tortilla", cut.Find(".account-preview-lines").TextContent);
        Assert.Contains("2 × Queso", cut.Find(".account-preview-lines").TextContent);
        Assert.Contains("-6,00 €", cut.Find(".account-preview-discount").TextContent);
        Assert.Contains("IVA (10 %)", cut.Find(".account-preview-total").TextContent);
        Assert.Contains("4,09 €", cut.Find(".account-preview-total").TextContent);
        Assert.Contains("IVA (21 %)", cut.Find(".account-preview-total").TextContent);
        Assert.Contains("1,56 €", cut.Find(".account-preview-total").TextContent);
        Assert.Contains("27,00 €", cut.Find(".account-preview-per-person").TextContent);

        cut.Find("button[aria-label='Imprimir cuenta']").Click();
        cut.Find("button[aria-label='Cerrar cuenta']").Click();
        Assert.False(GetField<bool>(cut.Instance, "accountPreviewOpen"));
    }

    [Fact]
    public async Task Account_tax_breakdown_is_hidden_when_any_active_amount_has_an_unknown_tax_rate()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId) with
        {
            Orders = [new(orderId, "Delivered", "Pending", 20m, 0m, DateTime.UtcNow)],
        };
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill);
        PropertyInfo taxBreakdown = typeof(PosHome).GetProperty(
            "BillTaxBreakdown",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        SetField(cut.Instance, "bill", null);
        Assert.Empty((IEnumerable)taxBreakdown.GetValue(cut.Instance)!);

        SetField(cut.Instance, "bill", bill);
        SetField(cut.Instance, "billOrders", new Dictionary<Guid, OrderDetailResponse>());
        Assert.Empty((IEnumerable)taxBreakdown.GetValue(cut.Instance)!);

        SetField(
            cut.Instance,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [orderId] = new(
                    orderId,
                    restaurantId,
                    "Delivered",
                    20m,
                    [
                        new(
                            lineId,
                            "Producto sin tasa",
                            20m,
                            1,
                            20m,
                            "KITCHEN",
                            "Cocina",
                            "Active",
                            null,
                            string.Empty
                        ),
                    ],
                    []
                ),
            }
        );

        Assert.Empty((IEnumerable)taxBreakdown.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task Opening_a_bill_loads_order_details_and_transfer_uses_the_outstanding_amount()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId) with
        {
            Orders = [new(orderId, "Submitted", "Pending", 25m, 0m, DateTime.UtcNow)],
            Outstanding = 25m,
        };
        OrderDetailResponse detail = new(orderId, restaurantId, "Submitted", 25m, [], []);
        BillingHandler handler = new(bill, detail);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(bill.Id, restaurantId, bill.TableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "hasSessionOrders", true);
        SetField(cut.Instance, "currentChargeAmount", 99m);
        SetField(cut.Instance, "tipPercentage", 10);
        SetField(cut.Instance, "tipAmount", 9m);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenBill"));

        Assert.Equal(bill.Id, GetField<SessionBillResponse>(cut.Instance, "bill").Id);
        OrderDetailResponse loadedDetail = GetField<Dictionary<Guid, OrderDetailResponse>>(
            cut.Instance,
            "billOrders"
        )[orderId];
        Assert.Equal(detail.Id, loadedDetail.Id);
        Assert.Equal(detail.Status, loadedDetail.Status);
        Assert.Equal(detail.Total, loadedDetail.Total);
        Assert.Equal(0m, GetField<decimal>(cut.Instance, "currentChargeAmount"));
        Assert.Equal(0, GetField<int>(cut.Instance, "tipPercentage"));
        Assert.Equal(0m, GetField<decimal>(cut.Instance, "tipAmount"));
        Assert.False(GetField<bool>(cut.Instance, "paymentDialogOpen"));

        SetField(cut.Instance, "paymentDialogOpen", true);
        SetField(cut.Instance, "paymentMethod", "Transfer");
        await cut.InvokeAsync(() => Invoke(cut.Instance, "LoadBill"));

        Assert.Equal(bill.Outstanding, GetField<decimal>(cut.Instance, "currentChargeAmount"));
        Assert.Equal(2, handler.BillRequests);
        Assert.Equal(2, handler.OrderRequests);
    }

    [Fact]
    public async Task Opening_a_bill_reports_load_failure_and_discards_partial_data()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        BillingHandler handler = new(bill, failBillRequest: true);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(bill.Id, restaurantId, bill.TableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "hasSessionOrders", true);
        SetField(cut.Instance, "bill", bill);
        SetField(
            cut.Instance,
            "billOrders",
            new Dictionary<Guid, OrderDetailResponse>
            {
                [Guid.NewGuid()] = new(Guid.NewGuid(), restaurantId, "Submitted", 10m, [], []),
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenBill"));

        Assert.Null(GetField<SessionBillResponse?>(cut.Instance, "bill"));
        Assert.Empty(GetField<Dictionary<Guid, OrderDetailResponse>>(cut.Instance, "billOrders"));
        Assert.False(string.IsNullOrWhiteSpace(GetField<string>(cut.Instance, "billLoadError")));
        Assert.False(string.IsNullOrWhiteSpace(GetField<string>(cut.Instance, "message")));
        Assert.Equal(1, handler.BillRequests);
        Assert.Equal(0, handler.OrderRequests);
    }

    [Fact]
    public async Task Opening_an_already_paid_bill_waits_for_the_sale_projection_before_offering_its_ticket()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId) with
        {
            Total = 12m,
            Outstanding = 0m,
            CanCheckout = false,
            Orders = [new(orderId, "Delivered", "Paid", 12m, 12m, DateTime.UtcNow)],
        };
        SaleResponse sale = new([new SalePaymentBreakdown("Card", 1, 12m)]);
        BillingHandler handler = new(
            bill,
            new OrderDetailResponse(orderId, restaurantId, "Delivered", 12m, [], []),
            sale: sale
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(bill.Id, restaurantId, bill.TableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "hasSessionOrders", true);

        await cut.InvokeAsync(() => Invoke(cut.Instance, "OpenBill"));

        Assert.Equal(bill.Id, GetField<Guid?>(cut.Instance, "readyTicketSessionId"));
        Assert.IsType<SaleResponse>(GetField<SaleResponse?>(cut.Instance, "salesTicket"));
        Assert.Contains("aria-label=\"Ver ticket de venta\"", cut.Markup);
    }

    [Fact]
    public async Task Releasing_a_billable_session_clears_pos_state_and_refreshes_operational_data()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        ReleaseSessionHandler handler = new(restaurantId, sessionId);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Pos", "Open", [])
        );
        SetField(
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
                Guid.Empty,
                "Sala",
                0
            )
        );
        SetField(
            cut.Instance,
            "bill",
            Bill(restaurantId) with
            {
                Id = sessionId,
                TableId = tableId,
                CanRelease = true,
            }
        );
        SetField(cut.Instance, "billOpen", true);
        SetField(cut.Instance, "paymentDialogOpen", true);
        SetField(cut.Instance, "hasSessionOrders", true);
        SetField(
            cut.Instance,
            "cart",
            new List<PosCartLine>
            {
                new() { BasePrice = 10m, Quantity = 1 },
            }
        );

        await cut.InvokeAsync(() => Invoke(cut.Instance, "ReleaseSession"));

        Assert.Equal($"/api/dining/sessions/{sessionId}/close", handler.ReleasePath);
        Assert.Equal(1, handler.TableReads);
        Assert.Equal(1, handler.CashRegisterReads);
        Assert.Equal(1, handler.OrderReads);
        Assert.Null(GetField<DiningSessionResponse?>(cut.Instance, "session"));
        Assert.Null(GetField<TableResponse?>(cut.Instance, "selectedTable"));
        Assert.Null(GetField<SessionBillResponse?>(cut.Instance, "bill"));
        Assert.Empty(GetField<List<PosCartLine>>(cut.Instance, "cart"));
        Assert.False(GetField<bool>(cut.Instance, "billOpen"));
        Assert.False(GetField<bool>(cut.Instance, "paymentDialogOpen"));
        Assert.False(GetField<bool>(cut.Instance, "hasSessionOrders"));
        Assert.Contains("Mesa liberada desde TPV.", GetField<string>(cut.Instance, "message"));
    }

    [Fact]
    public async Task Realtime_refresh_keeps_guest_count_prompt_and_replaces_the_active_session()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DiningSessionResponse refreshedSession = new(
            sessionId,
            restaurantId,
            tableId,
            "Pos",
            "Open",
            [new(Guid.NewGuid(), "Submitted", "Pending", 12m)],
            true,
            null
        );
        OperationalRefreshHandler handler = new(restaurantId) { Session = refreshedSession };
        TableResponse table = new(
            tableId,
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            Guid.NewGuid(),
            "Sala",
            0
        );
        handler.Tables.Add(table);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "guestCountDialogOpen", true);
        SetField(cut.Instance, "tables", new List<TableResponse> { table });

        await cut.InvokeAsync(() =>
            Invoke(cut.Instance, "RefreshActiveSessionAfterOrderEventAsync")
        );

        Assert.Equal(1, handler.ActiveSessionReads);
        DiningSessionResponse updatedSession = GetField<DiningSessionResponse>(
            cut.Instance,
            "session"
        );
        Assert.Equal(sessionId, updatedSession.Id);
        Assert.Equal(refreshedSession.Orders.Single(), updatedSession.Orders.Single());
        Assert.True(GetField<bool>(cut.Instance, "hasSessionOrders"));
        Assert.Equal(table, GetField<TableResponse?>(cut.Instance, "guestCountTable"));
        Assert.Null(GetField<TableResponse?>(cut.Instance, "selectedTable"));
    }

    [Fact]
    public async Task Realtime_refresh_returns_to_tables_when_session_is_no_longer_active()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        OperationalRefreshHandler handler = new(restaurantId);
        handler.Tables.Add(
            new TableResponse(
                tableId,
                restaurantId,
                "M1",
                "Mesa 1",
                true,
                "Available",
                null,
                Guid.NewGuid(),
                "Sala",
                0
            )
        );
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(sessionId, restaurantId, tableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "tables", handler.Tables);
        SetField(cut.Instance, "selectedTable", handler.Tables.Single());

        await cut.InvokeAsync(() =>
            Invoke(cut.Instance, "RefreshActiveSessionAfterOrderEventAsync")
        );

        Assert.Null(GetField<DiningSessionResponse?>(cut.Instance, "session"));
        Assert.Null(GetField<TableResponse?>(cut.Instance, "selectedTable"));
        Assert.Equal(0, handler.ActiveSessionReads);
    }

    [Fact]
    public async Task Cancelling_a_pending_line_refreshes_the_bill_and_reports_the_result()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        OrderLineDetailResponse line = new(
            lineId,
            "Café",
            2m,
            1,
            2m,
            "BAR",
            "Barra",
            "Active",
            null,
            string.Empty
        );
        OrderDetailResponse order = new(orderId, restaurantId, "Submitted", 2m, [line], []);
        SessionBillResponse bill = Bill(restaurantId) with
        {
            Id = sessionId,
            Orders = [new(orderId, "Submitted", "Pending", 2m, 0m, DateTime.UtcNow)],
            Total = 2m,
            Outstanding = 2m,
        };
        CancelBillLineHandler handler = new(bill, order);
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(sessionId, restaurantId, bill.TableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "bill", bill);
        SetField(cut.Instance, "billOpen", true);
        SetField(cut.Instance, "hasSessionOrders", true);

        await cut.InvokeAsync(async () =>
        {
            MethodInfo cancel = typeof(PosHome).GetMethod(
                "CancelLine",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)cancel.Invoke(cut.Instance, [bill.Orders[0], line])!;
        });

        Assert.Equal($"/api/orders/{orderId}/lines/{lineId}/cancel", handler.CancelPath);
        Assert.Equal("Cancelado desde TPV", handler.CancellationReason);
        Assert.Equal(1, handler.BillReads);
        Assert.Equal(1, handler.OrderReads);
        Assert.Equal(
            "Cancelled",
            GetField<Dictionary<Guid, OrderDetailResponse>>(cut.Instance, "billOrders")[orderId]
                .Lines[0]
                .Status
        );
        Assert.Contains(
            "Café cancelado antes de preparación.",
            GetField<string>(cut.Instance, "message")
        );
        Assert.True(GetField<bool>(cut.Instance, "success"));
    }

    [Fact]
    public void Pos_labels_cover_supported_and_unknown_values()
    {
        AssertLabel("FormatLabel", "PLATO", "Plato");
        AssertLabel("FormatLabel", "RACION", "Ración");
        AssertLabel("FormatLabel", "MEDIA_RACION", "Media ración");
        AssertLabel("FormatLabel", "TAPA", "Tapa");
        AssertLabel("FormatLabel", "UNIDAD", "Unidad");
        AssertLabel("FormatLabel", "TERCIO", "Tercio");
        AssertLabel("FormatLabel", "VASO", "Vaso");
        AssertLabel("FormatLabel", "COPA", "Copa");
        AssertLabel("FormatLabel", "COMBO", "Combo");
        AssertLabel("FormatLabel", "Formato personalizado", "Formato personalizado");

        AssertLabel("SessionSourceLabel", "Pos", "TPV");
        AssertLabel("SessionSourceLabel", "Comandero", "Comandero");
        AssertLabel("SessionSourceLabel", "CustomerQr", "QR");
        AssertLabel("SessionSourceLabel", "Other", "Other");
        AssertLabel("PickupPaymentLabel", "Open", "Pendiente");
        AssertLabel("PickupPaymentLabel", "Paid", "Pagado");
        AssertLabel("PickupPaymentLabel", "Refunded", "Reembolsado");
        AssertLabel("PickupPaymentLabel", "Unknown", "Sin sincronizar");
        AssertLabel("PaymentLabel", "Cash", "Efectivo");
        AssertLabel("PaymentLabel", "Card", "Tarjeta / datáfono");
        AssertLabel("PaymentLabel", "Transfer", "Transferencia");
        AssertLabel("PaymentLabel", "Online", "Pago online");
        AssertLabel("PaymentLabel", "Cheque", "Cheque");
        AssertLabel("PaymentLabel", "Other", "Other");
        AssertLabel("StatusLabel", "Occupied", "Ocupada");
        AssertLabel("StatusLabel", "Available", "Disponible");

        Assert.Equal("Preparando", InvokeStatic<string>("KitchenLineStatusLabel", "InPreparation"));
        Assert.Equal(
            "Esperando despacho del chef",
            InvokeStatic<string>("KitchenLineStatusLabel", "Ready")
        );
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
    public void Kitchen_clock_excludes_cancelled_and_completed_dispatched_orders()
    {
        DateTime submittedAt = DateTime.UtcNow.AddMinutes(-5);
        MethodInfo kitchenClock = typeof(PosHome).GetMethod(
            "KitchenClock",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;

        Assert.Null(kitchenClock.Invoke(null, [Order(status: "Cancelled", submittedAt, [])]));
        Assert.Null(
            kitchenClock.Invoke(
                null,
                [Order(status: "Submitted", submittedAt, []) with { SubmittedAtUtc = null }]
            )
        );
        Assert.Null(
            kitchenClock.Invoke(
                null,
                [Order(status: "Submitted", submittedAt, [new("BAR", "Barra", "Cancelled")])]
            )
        );
        Assert.Null(
            kitchenClock.Invoke(
                null,
                [
                    Order(
                        status: "Ready",
                        submittedAt,
                        [new("BAR", "Barra", "Dispatched", submittedAt.AddMinutes(2))]
                    ),
                ]
            )
        );
        Assert.Null(
            kitchenClock.Invoke(
                null,
                [Order(status: "Submitted", submittedAt, [new("BAR", "Barra", "Ready")])]
            )
        );

        object? runningClock = kitchenClock.Invoke(
            null,
            [Order(status: "Submitted", submittedAt, [new("BAR", "Barra", "InPreparation")])]
        );
        (DateTime Start, DateTime? End) running = Assert.IsType<ValueTuple<DateTime, DateTime?>>(
            runningClock
        );
        Assert.Equal(submittedAt, running.Start);
        Assert.Null(running.End);

        object? readyClock = kitchenClock.Invoke(
            null,
            [
                Order(
                    status: "Submitted",
                    submittedAt,
                    [new("BAR", "Barra", "Ready", submittedAt.AddMinutes(-1))]
                ),
            ]
        );
        (DateTime Start, DateTime? End) completed = Assert.IsType<ValueTuple<DateTime, DateTime?>>(
            readyClock
        );
        Assert.Equal(submittedAt, completed.Start);
        Assert.Equal(submittedAt, completed.End);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Delivered")]
    public void Kitchen_clock_excludes_orders_that_are_not_active(string status)
    {
        DateTime submittedAt = DateTime.UtcNow.AddMinutes(-5);
        object? clock = InvokeStatic<object?>(
            "KitchenClock",
            Order(status, submittedAt, [new("BAR", "Barra", "InPreparation")])
        );

        Assert.Null(clock);
    }

    [Fact]
    public void Kitchen_clock_uses_latest_ready_time_and_never_precedes_submission()
    {
        DateTime submittedAt = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        object? latestReady = InvokeStatic<object?>(
            "KitchenClock",
            Order(
                "Submitted",
                submittedAt,
                [
                    new("KITCHEN", "Cocina", "Ready", submittedAt.AddMinutes(4)),
                    new("BAR", "Barra", "Dispatched", submittedAt.AddMinutes(9)),
                ]
            )
        );
        object? timestampBeforeSubmission = InvokeStatic<object?>(
            "KitchenClock",
            Order(
                "Submitted",
                submittedAt,
                [new("BAR", "Barra", "Ready", submittedAt.AddMinutes(-1))]
            )
        );

        Assert.Equal(
            submittedAt.AddMinutes(9),
            Assert.IsType<ValueTuple<DateTime, DateTime?>>(latestReady).Item2
        );
        Assert.Equal(
            submittedAt,
            Assert.IsType<ValueTuple<DateTime, DateTime?>>(timestampBeforeSubmission).Item2
        );
    }

    [Fact]
    public void Kitchen_duration_formats_hours_and_clamps_negative_elapsed_time()
    {
        Assert.Equal(
            "1:02:03",
            InvokeStatic<string>("FormatKitchenDuration", TimeSpan.FromSeconds(3723))
        );
        Assert.Equal(
            "00:00",
            InvokeStatic<string>("FormatKitchenDuration", TimeSpan.FromSeconds(-1))
        );
        Assert.Equal(
            "00:00",
            InvokeStatic<string>("FormatKitchenDuration", TimeSpan.FromMilliseconds(999))
        );
    }

    [Fact]
    public async Task Table_kitchen_time_shows_the_longest_active_order_duration()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, []);
        TableResponse table = new(
            tableId,
            restaurantId,
            "M1",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            Guid.NewGuid(),
            "Sala",
            2
        );
        DateTime submittedAt = DateTime.UtcNow.AddMinutes(-30);
        SetField(
            cut.Instance,
            "activeOrders",
            new List<OrderResponse>
            {
                Order(
                    "Submitted",
                    submittedAt,
                    [new("KITCHEN", "Cocina", "Ready", submittedAt.AddMinutes(12))]
                ) with
                {
                    TableId = tableId,
                    DiningSessionId = sessionId,
                },
                Order(
                    "Submitted",
                    submittedAt,
                    [new("BAR", "Barra", "Ready", submittedAt.AddMinutes(20))]
                ) with
                {
                    TableId = tableId,
                    DiningSessionId = sessionId,
                },
            }
        );

        string? kitchenTime = (string?)
            typeof(PosHome)
                .GetMethod("TableKitchenTime", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(cut.Instance, [table]);

        Assert.Equal("20:00", kitchenTime);
    }

    [Fact]
    public async Task Pos_actions_without_required_state_return_without_changing_the_cart()
    {
        IRenderedComponent<PosHome> cut = await RenderTakeaway(Guid.NewGuid(), []);

        await cut.InvokeAsync(async () =>
        {
            InvokeVoid(cut.Instance, "SelectProduct", Guid.NewGuid());
            InvokeVoid(cut.Instance, "ChangeCartQuantity", Guid.NewGuid(), 1);
            InvokeVoid(cut.Instance, "SetExactAmount");
            InvokeVoid(cut.Instance, "OpenAccountPreview");
            InvokeVoid(cut.Instance, "OpenDiscountDialog");
            await Invoke(cut.Instance, "AcceptGuestCount");
            await Invoke(cut.Instance, "CancelSession");
            await Invoke(cut.Instance, "DeliverPickup");
            await Invoke(cut.Instance, "RefreshPickup");
            await Invoke(cut.Instance, "OpenBill");
            await Invoke(cut.Instance, "OpenSalesTicket");
            await Invoke(cut.Instance, "ReleaseSession");
            await InvokeTask(cut.Instance, "RefreshSalesTicketAsync", Guid.NewGuid());
            await Invoke(cut.Instance, "CreateCashMovement");
        });

        List<PosCartLine> cart = GetField<List<PosCartLine>>(cut.Instance, "cart");
        Assert.Empty(cart);
        Assert.False(GetField<bool>(cut.Instance, "accountPreviewOpen"));
        Assert.False(GetField<bool>(cut.Instance, "discountDialogOpen"));
        Assert.False(GetField<bool>(cut.Instance, "salesTicketOpen"));
        Assert.Empty(
            (List<PosApi.CheckoutOrderAllocation>)
                typeof(PosHome)
                    .GetMethod(
                        "GetCurrentAllocations",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )!
                    .Invoke(cut.Instance, null)!
        );
    }

    [Fact]
    public async Task Cash_movement_dialog_resets_its_draft_before_opening()
    {
        IRenderedComponent<PosHome> cut = await RenderTakeaway(Guid.NewGuid(), []);
        SetField(cut.Instance, "cashMovementType", "Income");
        SetField(cut.Instance, "cashMovementCategory", "Aportación de caja");
        SetField(cut.Instance, "cashMovementDescription", "Texto anterior");
        SetField(cut.Instance, "cashMovementAmount", 12m);

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "OpenCashMovements"));

        Assert.True(GetField<bool>(cut.Instance, "cashMovementsOpen"));
        Assert.Equal("Expense", GetField<string>(cut.Instance, "cashMovementType"));
        Assert.Equal("Pago a proveedor", GetField<string>(cut.Instance, "cashMovementCategory"));
        Assert.Equal(string.Empty, GetField<string>(cut.Instance, "cashMovementDescription"));
        Assert.Equal(0m, GetField<decimal>(cut.Instance, "cashMovementAmount"));

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "CloseCashMovements"));
        Assert.False(GetField<bool>(cut.Instance, "cashMovementsOpen"));
    }

    [Fact]
    public async Task Tip_selection_and_transfer_payment_keep_the_charge_keypad_consistent()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill);
        SetField(cut.Instance, "currentChargeAmount", bill.Outstanding);
        SetField(cut.Instance, "keypadTarget", "Tip");

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "SelectTip", 10));

        Assert.Equal(6m, GetField<decimal>(cut.Instance, "tipAmount"));
        Assert.Equal("6,00", GetField<string>(cut.Instance, "keypadText"));

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "SetPaymentMethod", "Transfer"));
        Assert.Equal(bill.Outstanding, GetField<decimal>(cut.Instance, "currentChargeAmount"));
        Assert.Equal("Charge", GetField<string>(cut.Instance, "keypadTarget"));

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "SelectKeypadTarget", "Charge"));
        Assert.Equal(bill.Outstanding, GetField<decimal>(cut.Instance, "currentChargeAmount"));

        await cut.InvokeAsync(() => InvokeVoid(cut.Instance, "SetPaymentMethod", "Cash"));
        Assert.Equal(0m, GetField<decimal>(cut.Instance, "currentChargeAmount"));
        Assert.Equal("Cash", GetField<string>(cut.Instance, "paymentMethod"));
    }

    private static OrderResponse Order(
        string status,
        DateTime submittedAt,
        List<OrderStationProgressResponse> stations
    ) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            string.Empty,
            "DineIn",
            "OnAccount",
            status,
            0m,
            submittedAt,
            submittedAt,
            stations
        );

    private static void AssertLabel(string method, string value, string expected) =>
        Assert.Equal(expected, InvokeStatic<string>(method, value));

    private static T InvokeStatic<T>(string methodName, params object?[] args) =>
        (T)
            typeof(PosHome)
                .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, args)!;

    private static SessionBillResponse Bill(Guid restaurantId)
    {
        Guid tableId = Guid.NewGuid();
        return new(
            Guid.NewGuid(),
            restaurantId,
            tableId,
            "Open",
            string.Empty,
            null,
            60m,
            60m,
            true,
            false,
            [
                new(Guid.NewGuid(), "Delivered", "Pending", 25m, 0m, DateTime.UtcNow),
                new(Guid.NewGuid(), "Delivered", "Pending", 35m, 0m, DateTime.UtcNow),
                new(Guid.NewGuid(), "Cancelled", "Cancelled", 100m, 0m, DateTime.UtcNow),
            ]
        );
    }

    private static CashRegisterResponse OpenRegister(Guid restaurantId) =>
        new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.Today),
            "Open",
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            null,
            null,
            null,
            DateTime.UtcNow,
            "Cajero",
            null,
            null,
            [],
            [],
            [],
            []
        );

    private async Task<IRenderedComponent<PosHome>> RenderPayment(
        Guid restaurantId,
        SessionBillResponse bill,
        HttpMessageHandler? handler = null
    )
    {
        IRenderedComponent<PosHome> cut = await RenderTakeaway(restaurantId, [], handler);
        SetField(cut.Instance, "takeawayOpen", false);
        SetField(cut.Instance, "serviceMode", "DineIn");
        SetField(
            cut.Instance,
            "selectedTable",
            new TableResponse(
                bill.TableId,
                restaurantId,
                "M1",
                "Mesa 1",
                true,
                "Occupied",
                bill.Id,
                Guid.NewGuid(),
                "Sala",
                0
            )
        );
        SetField(
            cut.Instance,
            "session",
            new DiningSessionResponse(bill.Id, restaurantId, bill.TableId, "Pos", "Open", [])
        );
        SetField(cut.Instance, "bill", bill);
        SetField(cut.Instance, "billOpen", true);
        SetField(
            cut.Instance,
            "cashRegister",
            new CashRegisterResponse(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.Today),
                "Open",
                100m,
                0m,
                0m,
                0m,
                0m,
                100m,
                100m,
                null,
                null,
                null,
                DateTime.UtcNow,
                "Cajero",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );
        await Rerender(cut);
        return cut;
    }

    private static async Task<WebApplication> StartRealtimeHubAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR();
        WebApplication app = builder.Build();
        app.MapHub<PosTestRealtimeHub>("/hubs/orders");
        app.MapHub<PosTestRealtimeHub>("/hubs/catalog");
        await app.StartAsync();
        return app;
    }

    private async Task<IRenderedComponent<PosHome>> RenderTakeaway(
        Guid restaurantId,
        List<MenuItemResponse> menu,
        HttpMessageHandler? handler = null,
        IJSRuntime? jsRuntime = null
    )
    {
        HttpClient http = new(handler ?? new EmptyHandler())
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new OrderRealtimeClient(http, api));
        Services.AddSingleton(new CatalogRealtimeClient(http, api));
        Services.AddSingleton<IJSRuntime>(jsRuntime ?? new EmptyJsRuntime());

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        SetField(
            cut.Instance,
            "login",
            new LoginResponse(
                "token",
                DateTime.UtcNow.AddHours(1),
                new LoginUser(Guid.NewGuid(), "cajero", "Cajero"),
                []
            )
        );
        SetField(cut.Instance, "initializing", false);
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(
            cut.Instance,
            "restaurants",
            new List<RestaurantResponse>
            {
                new(restaurantId, "CENTRO", "Centro", "Calle Mayor", true),
            }
        );
        SetField(cut.Instance, "menu", menu);
        SetField(cut.Instance, "takeawayOpen", true);
        SetField(cut.Instance, "serviceMode", "Takeaway");
        await Rerender(cut);
        return cut;
    }

    private static Task Rerender(IRenderedComponent<PosHome> cut) =>
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.Empty));

    private static T GetField<T>(object component, string name) =>
        (T)
            component
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(component)!;

    private static Task Invoke(object component, string methodName) =>
        (Task)
            component
                .GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, null)!;

    private static Task InvokeTask(object component, string methodName, params object?[] args) =>
        (Task)
            component
                .GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, args)!;

    private static void InvokeVoid(object component, string methodName, params object?[] args) =>
        component
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, args);

    private static void SetField(object component, string name, object? value) =>
        component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class ProviderSettingsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(
                            new ProviderSettingsResponse("", false, false)
                        ),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
            );
    }

    private sealed class EmptyAccessibleRestaurantsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Array.Empty<RestaurantResponse>()),
                }
            );
    }

    private sealed class LoginBootstrapHandler(
        Guid firstId,
        Guid secondId,
        TableResponse firstTable,
        TableResponse secondTable
    ) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/api/identity/auth/provider")
                return Task.FromResult(Json(new ProviderSettingsResponse("local", false, false)));
            if (path == "/api/identity/auth/exchange" && request.Method == HttpMethod.Post)
                return Task.FromResult(
                    Json(
                        new LoginResponse(
                            "exchanged-token",
                            DateTime.UtcNow.AddHours(1),
                            new LoginUser(Guid.NewGuid(), "cajero", "Cajero"),
                            []
                        )
                    )
                );
            if (path == "/api/restaurant-operations/restaurants/accessible/pos")
                return Task.FromResult(
                    Json(
                        new[]
                        {
                            new RestaurantResponse(firstId, "CENTRO", "Centro", "Mayor", true),
                            new RestaurantResponse(secondId, "NORTE", "Norte", "Norte", true),
                        }
                    )
                );
            if (path == $"/api/dining/restaurants/{firstId}/tables")
                return Task.FromResult(Json(new[] { firstTable }));
            if (path == $"/api/dining/restaurants/{secondId}/tables")
                return Task.FromResult(Json(new[] { secondTable }));
            if (path == "/api/orders/pos")
                return Task.FromResult(Json(Array.Empty<OrderResponse>()));
            if (path.EndsWith("/current", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class PosTestRealtimeHub : Hub
    {
        public Task JoinPos(Guid restaurantId) =>
            Groups.AddToGroupAsync(Context.ConnectionId, PosGroup(restaurantId));

        public Task LeavePos(Guid restaurantId) =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroup(restaurantId));

        public Task NotifyDiningTableUpdated(Guid restaurantId) =>
            Task.WhenAll(
                Clients.Group(PosGroup(restaurantId)).SendAsync("DiningTableUpdated", restaurantId),
                Clients
                    .Group(CommanderGroup(restaurantId))
                    .SendAsync("DiningTableUpdated", restaurantId)
            );

        public Task JoinRestaurant(Guid restaurantId) =>
            Groups.AddToGroupAsync(Context.ConnectionId, CatalogGroup(restaurantId));

        public Task LeaveRestaurant(Guid restaurantId) =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, CatalogGroup(restaurantId));

        private static string PosGroup(Guid restaurantId) =>
            $"restaurant:{restaurantId:N}:pos-orders";

        private static string CommanderGroup(Guid restaurantId) =>
            $"restaurant:{restaurantId:N}:commander-orders";

        private static string CatalogGroup(Guid restaurantId) =>
            $"restaurant:{restaurantId:N}:catalog";
    }

    private sealed class OperationalRefreshHandler(Guid restaurantId) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<TableResponse> Tables { get; } = [];
        public List<MenuItemResponse> Menu { get; } = [];
        public DiningSessionResponse? Session { get; init; }
        public bool FailMenu { get; set; }
        public bool FailTables { get; set; }
        public int MenuReads { get; private set; }
        public int ActiveSessionReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
            {
                MenuReads++;
                return Task.FromResult(
                    FailMenu
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        : Json(Menu)
                );
            }
            if (path == $"/api/dining/restaurants/{restaurantId}/tables")
                return Task.FromResult(
                    FailTables
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        : Json(Tables)
                );
            if (path == "/api/orders/pos")
                return Task.FromResult(Json(Array.Empty<OrderResponse>()));
            if (
                Session is not null
                && path == $"/api/dining/tables/{Session.TableId}/active-session"
            )
            {
                ActiveSessionReads++;
                return Task.FromResult(Json(Session));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class GuestCountHandler(Guid restaurantId, Guid tableId, Guid sessionId)
        : HttpMessageHandler
    {
        public int? GuestCount { get; private set; }
        public bool MenuWasLoaded { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == $"/api/dining/tables/{tableId}/active-session")
                return Json(
                    new DiningSessionResponse(
                        sessionId,
                        restaurantId,
                        tableId,
                        "Pos",
                        "Open",
                        [],
                        true,
                        null
                    )
                );
            if (path == $"/api/dining/sessions/{sessionId}/guests")
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                GuestCount = body.RootElement.GetProperty("guestCount").GetInt32();
                return Json(
                    new DiningSessionResponse(
                        sessionId,
                        restaurantId,
                        tableId,
                        "Pos",
                        "Open",
                        [],
                        true,
                        GuestCount
                    )
                );
            }
            if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
            {
                MenuWasLoaded = true;
                return Json(Array.Empty<MenuItemResponse>());
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class OpenSessionCancellationHandler(
        Guid restaurantId,
        Guid tableId,
        Guid sessionId
    ) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public bool MenuWasLoaded { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (
                request.Method == HttpMethod.Post
                && path == $"/api/dining/tables/{tableId}/sessions"
            )
            {
                return Task.FromResult(Json(Session("Open")));
            }
            if (
                request.Method == HttpMethod.Post
                && path == $"/api/dining/sessions/{sessionId}/cancel"
            )
            {
                return Task.FromResult(Json(Session("Cancelled")));
            }
            if (
                request.Method == HttpMethod.Put
                && path == $"/api/dining/sessions/{sessionId}/guests"
            )
            {
                return Task.FromResult(Json(Session("Open") with { GuestCount = 2 }));
            }
            if (path == $"/api/dining/restaurants/{restaurantId}/tables")
            {
                return Task.FromResult(Json(Array.Empty<TableResponse>()));
            }
            if (path == $"/api/cash-register/restaurants/{restaurantId}/current")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (path == "/api/orders/pos")
            {
                return Task.FromResult(Json(Array.Empty<OrderResponse>()));
            }
            if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
            {
                MenuWasLoaded = true;
                return Task.FromResult(Json(Array.Empty<MenuItemResponse>()));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private DiningSessionResponse Session(string status) =>
            new(sessionId, restaurantId, tableId, "Pos", status, [], true, null);

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class PickupLifecycleHandler(
        Guid restaurantId,
        OrderResponse pickup,
        OrderLineDetailResponse line
    ) : HttpMessageHandler
    {
        private string paymentStatus = "Open";
        public string? CancellationReason { get; private set; }
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (request.Method == HttpMethod.Get && path == "/api/orders/" + pickup.Id)
            {
                return Json(
                    new OrderDetailResponse(pickup.Id, restaurantId, "Submitted", 12m, [line], [])
                );
            }

            if (
                request.Method == HttpMethod.Post
                && path.StartsWith("/api/payments/restaurants/", StringComparison.Ordinal)
            )
                return Json(new[] { new OrderPaymentStatusResponse(pickup.Id, paymentStatus) });

            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/cancel", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                CancellationReason = body.RootElement.GetProperty("reason").GetString();
                return Json(
                    new OrderDetailResponse(
                        pickup.Id,
                        restaurantId,
                        "Submitted",
                        12m,
                        [
                            line with
                            {
                                Status = "Cancelled",
                                CancellationReason = CancellationReason!,
                            },
                        ],
                        []
                    )
                );
            }
            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/capture", StringComparison.Ordinal)
            )
            {
                paymentStatus = "Paid";
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/deliver", StringComparison.Ordinal)
            )
                return Json(pickup with { Status = "Delivered" });

            if (request.Method == HttpMethod.Get && path == "/api/orders/pos")
                return Json(new[] { pickup with { Status = "Delivered" } });

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class TakeawayOrderHandler(OrderResponse submitted) : HttpMessageHandler
    {
        public JsonElement? CreatedPayload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/orders")
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                CreatedPayload = body.RootElement.Clone();
                return Json(submitted with { Status = "Draft", SubmittedAtUtc = null });
            }

            if (
                request.Method == HttpMethod.Post
                && path == "/api/orders/" + submitted.Id + "/submit"
            )
                return Json(submitted);

            if (request.Method == HttpMethod.Get && path == "/api/orders/pos")
                return Json(new[] { submitted });

            if (
                request.Method == HttpMethod.Post
                && path.StartsWith("/api/payments/restaurants/", StringComparison.Ordinal)
            )
                return Json(Array.Empty<OrderPaymentStatusResponse>());

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class VisibleSessionHandler(
        Guid restaurantId,
        Guid currentTableId,
        Guid currentSessionId,
        Guid staleTableId,
        Guid failedTableId
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            string currentPath = "/api/dining/tables/" + currentTableId + "/active-session";
            if (path == currentPath)
            {
                return Task.FromResult(
                    Json(
                        new DiningSessionResponse(
                            currentSessionId,
                            restaurantId,
                            currentTableId,
                            "Pos",
                            "Open",
                            []
                        )
                    )
                );
            }

            string stalePath = "/api/dining/tables/" + staleTableId + "/active-session";
            if (path == stalePath)
            {
                return Task.FromResult(
                    Json(
                        new DiningSessionResponse(
                            Guid.NewGuid(),
                            restaurantId,
                            staleTableId,
                            "Pos",
                            "Open",
                            []
                        )
                    )
                );
            }

            string failedPath = "/api/dining/tables/" + failedTableId + "/active-session";
            if (path == failedPath)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class DelayedVisibleSessionHandler(
        Guid restaurantId,
        Guid tableId,
        Guid sessionId
    ) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> response = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount { get; private set; }

        public void CompleteRequest() =>
            response.SetResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        new DiningSessionResponse(
                            sessionId,
                            restaurantId,
                            tableId,
                            "Pos",
                            "Open",
                            []
                        )
                    ),
                }
            );

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (
                request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath
                    == "/api/dining/tables/" + tableId + "/active-session"
            )
            {
                RequestCount++;
                RequestStarted.TrySetResult();
                return response.Task;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class OpenCashRegisterHandler(CashRegisterResponse opened) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            request.Method == HttpMethod.Post
            && request.RequestUri!.AbsolutePath.EndsWith("/open", StringComparison.Ordinal)
                ? Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(opened),
                    }
                )
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class CloseCashRegisterHandler(CashRegisterResponse closed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            request.Method == HttpMethod.Post
            && request.RequestUri!.AbsolutePath.EndsWith("/close", StringComparison.Ordinal)
                ? Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(closed),
                    }
                )
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class CashMovementHandler(CashRegisterResponse updated) : HttpMessageHandler
    {
        public string? Type { get; private set; }
        public string? Method { get; private set; }
        public decimal Amount { get; private set; }
        public string? Category { get; private set; }
        public string? Description { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken)
            );
            JsonElement movement = body.RootElement;
            Type = movement.GetProperty("type").GetString();
            Method = movement.GetProperty("method").GetString();
            Amount = movement.GetProperty("amount").GetDecimal();
            Category = movement.GetProperty("category").GetString();
            Description = movement.GetProperty("description").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(updated),
            };
        }
    }

    private sealed class SaleTicketHandler(SaleResponse sale) : HttpMessageHandler
    {
        public bool Ready { get; set; } = true;
        public HttpStatusCode? FailureStatus { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                ? Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(
                            new ProviderSettingsResponse("", false, false)
                        ),
                    }
                )
            : FailureStatus is HttpStatusCode failureStatus
            && request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath.StartsWith("/api/sales/", StringComparison.Ordinal)
                ? Task.FromResult(new HttpResponseMessage(failureStatus))
            : Ready
            && request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath.StartsWith("/api/sales/", StringComparison.Ordinal)
                ? Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(sale),
                    }
                )
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class DeferredSaleTicketHandler(SaleResponse sale) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> response = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public TaskCompletionSource SaleRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void CompleteSaleRequest() =>
            response.TrySetResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(sale) }
            );

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath == "/api/identity/auth/provider"
                ? Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(
                            new ProviderSettingsResponse("", false, false)
                        ),
                    }
                )
            : request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath.StartsWith("/api/sales/", StringComparison.Ordinal)
                ? WaitForSaleAsync()
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        private Task<HttpResponseMessage> WaitForSaleAsync()
        {
            SaleRequestStarted.TrySetResult();
            return response.Task;
        }
    }

    private sealed class CheckoutHandler(SessionBillResponse checkoutResult) : HttpMessageHandler
    {
        public JsonElement? CheckoutRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (
                request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/checkout", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                CheckoutRequest = body.RootElement.Clone();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(checkoutResult),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class BillingHandler(
        SessionBillResponse bill,
        OrderDetailResponse? detail = null,
        bool failBillRequest = false,
        SaleResponse? sale = null
    ) : HttpMessageHandler
    {
        public int BillRequests { get; private set; }
        public int OrderRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/bill", StringComparison.Ordinal)
            )
            {
                BillRequests++;
                return Task.FromResult(
                    failBillRequest
                        ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        : Json(bill)
                );
            }

            if (
                request.Method == HttpMethod.Get
                && path == "/api/sales/" + bill.Id
                && sale is not null
            )
            {
                return Task.FromResult(Json(sale));
            }

            if (
                request.Method == HttpMethod.Get
                && path.StartsWith("/api/orders/", StringComparison.Ordinal)
            )
            {
                OrderRequests++;
                return Task.FromResult(
                    detail is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(detail)
                );
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class ReleaseSessionHandler(Guid restaurantId, Guid sessionId)
        : HttpMessageHandler
    {
        public string? ReleasePath { get; private set; }
        public int TableReads { get; private set; }
        public int CashRegisterReads { get; private set; }
        public int OrderReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/close", StringComparison.Ordinal)
            )
            {
                ReleasePath = path;
                return Task.FromResult(
                    Json(
                        new DiningSessionResponse(
                            sessionId,
                            restaurantId,
                            Guid.NewGuid(),
                            "Pos",
                            "Closed",
                            []
                        )
                    )
                );
            }
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/tables", StringComparison.Ordinal)
            )
            {
                TableReads++;
                return Task.FromResult(Json(Array.Empty<TableResponse>()));
            }
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/current", StringComparison.Ordinal)
            )
            {
                CashRegisterReads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (request.Method == HttpMethod.Get && path == "/api/orders/pos")
            {
                OrderReads++;
                return Task.FromResult(Json(Array.Empty<OrderResponse>()));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class CancelBillLineHandler(SessionBillResponse bill, OrderDetailResponse order)
        : HttpMessageHandler
    {
        public string? CancelPath { get; private set; }
        public string? CancellationReason { get; private set; }
        public int BillReads { get; private set; }
        public int OrderReads { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (
                request.Method == HttpMethod.Post
                && path.EndsWith("/cancel", StringComparison.Ordinal)
            )
            {
                CancelPath = path;
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                CancellationReason = body.RootElement.GetProperty("reason").GetString();
                return Json(
                    order with
                    {
                        Lines =
                        [
                            order.Lines[0] with
                            {
                                Status = "Cancelled",
                                CancellationReason = CancellationReason!,
                            },
                        ],
                    }
                );
            }
            if (
                request.Method == HttpMethod.Get
                && path.EndsWith("/bill", StringComparison.Ordinal)
            )
            {
                BillReads++;
                return Json(bill);
            }
            if (request.Method == HttpMethod.Get && path == "/api/orders/" + order.Id)
            {
                OrderReads++;
                return Json(
                    order with
                    {
                        Lines =
                        [
                            order.Lines[0] with
                            {
                                Status = "Cancelled",
                                CancellationReason = "Cancelado desde TPV",
                            },
                        ],
                    }
                );
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class DiscountHandler(SessionBillResponse initial) : HttpMessageHandler
    {
        public int Updates { get; private set; }
        public decimal LastPercentage { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (
                request.Method == HttpMethod.Put
                && request.RequestUri!.AbsolutePath.EndsWith("/discount", StringComparison.Ordinal)
            )
            {
                using JsonDocument body = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                LastPercentage = body.RootElement.GetProperty("percentage").GetDecimal();
                Updates++;
                decimal discount = Math.Round(
                    initial.Total * LastPercentage / 100m,
                    2,
                    MidpointRounding.AwayFromZero
                );
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        initial with
                        {
                            DiscountPercentage = LastPercentage,
                            DiscountAmount = discount,
                            Outstanding = initial.Total - discount,
                        }
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class PickupJsRuntime : IJSRuntime
    {
        public string? PromptResult { get; set; } = "Cambio de pedido";
        public bool Confirmation { get; set; } = true;
        public int ConfirmationCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            object? value = identifier switch
            {
                "prompt" => PromptResult,
                "confirm" => Confirm(),
                _ => default(TValue),
            };
            return ValueTask.FromResult((TValue)value!);
        }

        private bool Confirm()
        {
            ConfirmationCount++;
            return Confirmation;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StoredSessionJsRuntime(string json) : IJSRuntime
    {
        public bool Removed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "sessionStorage.getItem")
                return ValueTask.FromResult((TValue)(object)json);
            if (identifier == "sessionStorage.removeItem")
                Removed = true;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
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

    private sealed class ConfirmJsRuntime(bool confirmation = true) : IJSRuntime
    {
        public int ConfirmationCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(identifier == "confirm" ? Confirm<TValue>() : default!);

        private TValue Confirm<TValue>()
        {
            ConfirmationCount++;
            return (TValue)(object)confirmation;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
    }
}
