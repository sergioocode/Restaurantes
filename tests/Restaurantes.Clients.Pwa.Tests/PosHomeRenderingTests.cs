using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Pos.Pwa.Realtime;
using Xunit;
using PosHome = Restaurantes.Clients.Pos.Pwa.Pages.Home;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class PosHomeRenderingTests : BunitContext
{
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
    }

    [Fact]
    public async Task Payment_dialog_calculates_change_and_requires_exact_amount_for_transfer()
    {
        Guid restaurantId = Guid.NewGuid();
        SessionBillResponse bill = Bill(restaurantId);
        IRenderedComponent<PosHome> cut = await RenderPayment(restaurantId, bill);

        cut.Find(".session-bill-card .bill-actions button.primary").Click();
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
    }

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

    private async Task<IRenderedComponent<PosHome>> RenderTakeaway(
        Guid restaurantId,
        List<MenuItemResponse> menu,
        HttpMessageHandler? handler = null
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
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

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

    private static void SetField(object component, string name, object value) =>
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
}
