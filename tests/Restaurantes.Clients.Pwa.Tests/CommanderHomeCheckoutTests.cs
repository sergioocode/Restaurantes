using System.Net;
using System.Reflection;
using System.Text.Json;
using Bunit;
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
        Set(cut.Instance, "FilteredTables", new List<TableResponse> { table });
        cut.Render();

        Assert.Contains("Seleccionar ubicación", cut.Markup);
        Assert.Contains("Terraza", cut.Markup);
        Assert.Contains("1 disponibles", cut.Markup);

        Set(cut.Instance, "selectedZoneId", zoneId);
        cut.Render();
        Assert.Contains("Mesa 1", cut.Markup);
        Assert.Contains("Disponible · abrir", cut.Markup);

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
                [orderId] = new(orderId, restaurantId, "Submitted", 13.5m, [line], []),
            }
        );
        Set(cut.Instance, "billOpen", true);
        cut.Render();
        Assert.Contains("CONSUMO DE LA MESA", cut.Markup);
        Assert.Contains("Queso", cut.Markup);
        Assert.Contains("Pendiente de tus comandas", cut.Markup);
        Assert.Contains("Liberar mesa", cut.Markup);
        Assert.Contains("Cancelar", cut.Markup);

        Set(cut.Instance, "billOpen", false);
        Set(cut.Instance, "salesTicketOpen", true);
        cut.Render();
        Assert.Contains("TICKET DE VENTA", cut.Markup);
        Assert.Contains("Restaurante Centro SL", cut.Markup);
        Assert.Contains("centro@example.test", cut.Markup);

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
}
