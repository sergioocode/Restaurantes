using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Restaurantes.Clients.CustomerQr.Pwa.Api;
using Restaurantes.Clients.CustomerQr.Pwa.Models;
using Restaurantes.Clients.CustomerQr.Pwa.Pages;
using Xunit;
using CommanderApi = Restaurantes.Clients.Commander.Pwa.Api.CommanderApi;
using CommanderBill = Restaurantes.Clients.Commander.Pwa.Models.SessionBillResponse;
using CommanderBillOrder = Restaurantes.Clients.Commander.Pwa.Models.SessionBillOrderResponse;
using CommanderHome = Restaurantes.Clients.Commander.Pwa.Pages.Home;
using CommanderLogin = Restaurantes.Clients.Commander.Pwa.Models.LoginResponse;
using CommanderMenuItem = Restaurantes.Clients.Commander.Pwa.Models.MenuItemResponse;
using CommanderOrder = Restaurantes.Clients.Commander.Pwa.Models.OrderResponse;
using CommanderOrderDetail = Restaurantes.Clients.Commander.Pwa.Models.OrderDetailResponse;
using CommanderOrderLine = Restaurantes.Clients.Commander.Pwa.Models.OrderLineDetailResponse;
using CommanderOrderRealtime = Restaurantes.Clients.Commander.Pwa.Realtime.OrderRealtimeClient;
using CommanderSession = Restaurantes.Clients.Commander.Pwa.Models.DiningSessionResponse;
using CommanderTable = Restaurantes.Clients.Commander.Pwa.Models.TableResponse;
using PosApi = Restaurantes.Clients.Pos.Pwa.Api.PosApi;
using PosBillOrder = Restaurantes.Clients.Pos.Pwa.Models.SessionBillOrderResponse;
using PosCashRegister = Restaurantes.Clients.Pos.Pwa.Models.CashRegisterResponse;
using PosCatalogRealtime = Restaurantes.Clients.Pos.Pwa.Realtime.CatalogRealtimeClient;
using PosHome = Restaurantes.Clients.Pos.Pwa.Pages.Home;
using PosLogin = Restaurantes.Clients.Pos.Pwa.Models.LoginResponse;
using PosLoginUser = Restaurantes.Clients.Pos.Pwa.Models.LoginUser;
using PosOrder = Restaurantes.Clients.Pos.Pwa.Models.OrderResponse;
using PosOrderDetail = Restaurantes.Clients.Pos.Pwa.Models.OrderDetailResponse;
using PosOrderLine = Restaurantes.Clients.Pos.Pwa.Models.OrderLineDetailResponse;
using PosOrderRealtime = Restaurantes.Clients.Pos.Pwa.Realtime.OrderRealtimeClient;
using PosPaymentStatus = Restaurantes.Clients.Pos.Pwa.Models.OrderPaymentStatusResponse;
using PosRestaurant = Restaurantes.Clients.Pos.Pwa.Models.RestaurantResponse;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class QrOrderTests : BunitContext
{
    [Fact]
    public async Task Customer_qr_submission_creates_captures_and_sends_order()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        QrSessionEnvelope envelope = new(
            new QrTableResponse(tableId, restaurantId, "T1", "Mesa 1"),
            new QrDiningSessionResponse(sessionId, restaurantId, tableId, "Open"),
            "customer-token",
            true
        );
        List<string> requests = [];
        List<string> captureBodies = [];
        string? createBody = null;
        int submitAttempts = 0;
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add($"{request.Method} {path}");
                if (path == "/api/dining/qr/MESA-1" || path == "/api/dining/qr/MESA-1/sessions")
                    return JsonResponse(envelope);
                if (path == $"/api/restaurant-operations/restaurants/{restaurantId}")
                    return JsonResponse(
                        new RestaurantResponse(
                            restaurantId,
                            "REST-1",
                            "Restaurante de prueba",
                            "Calle de prueba",
                            true,
                            1,
                            DateTime.UtcNow
                        )
                    );
                if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
                    return JsonResponse(
                        new[]
                        {
                            new MenuItemResponse(
                                restaurantId,
                                productId,
                                "SKU-1",
                                "Producto",
                                Guid.NewGuid(),
                                "COMIDA",
                                "Comida",
                                12m,
                                true,
                                "GRILL",
                                "Parrilla"
                            ),
                        }
                    );
                if (path == "/api/orders" && request.Method == HttpMethod.Post)
                {
                    createBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return JsonResponse(
                        new OrderResponse(orderId, restaurantId, "Draft", 12m, 1, DateTime.UtcNow)
                    );
                }
                if (path == $"/api/payments/orders/{orderId}/capture-customer-qr")
                {
                    captureBodies.Add(
                        request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()
                    );
                    if (captureBodies.Count == 1)
                    {
                        return new HttpResponseMessage((HttpStatusCode)425)
                        {
                            Content = JsonContent.Create(new { title = "Pago en curso" }),
                        };
                    }
                    return JsonResponse(
                        new PaymentResponse(
                            Guid.NewGuid(),
                            orderId,
                            restaurantId,
                            12m,
                            "CustomerQr",
                            "Paid",
                            DateTime.UtcNow,
                            null
                        )
                    );
                }
                if (path == $"/api/orders/{orderId}/submit")
                {
                    submitAttempts++;
                    if (submitAttempts == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.Conflict)
                        {
                            Content = JsonContent.Create(new { title = "Proyección pendiente" }),
                        };
                    }
                    return JsonResponse(
                        new OrderResponse(
                            orderId,
                            restaurantId,
                            "Submitted",
                            12m,
                            2,
                            DateTime.UtcNow
                        )
                    );
                }
                if (path == $"/api/payments/restaurants/{restaurantId}/order-statuses")
                    return JsonResponse(Array.Empty<object>());
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CustomerQrApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<QrOrder> cut = Render<QrOrder>(parameters =>
            parameters.Add(component => component.QrCode, "MESA-1")
        );

        await cut.Find(".quantity-control button:last-child").ClickAsync();
        await cut.Find(".checkout-bar button").ClickAsync();

        Assert.Contains("POST /api/orders", requests);
        Assert.Contains($"POST /api/payments/orders/{orderId}/capture-customer-qr", requests);
        Assert.Contains($"POST /api/orders/{orderId}/submit", requests);
        Assert.Contains("Pedido enviado a cocina", cut.Markup);
        Assert.Contains("pagado y enviado.", cut.Markup);
        Assert.Equal(2, captureBodies.Count);
        Assert.Equal(2, submitAttempts);
        using JsonDocument firstCapture = JsonDocument.Parse(captureBodies[0]);
        using JsonDocument secondCapture = JsonDocument.Parse(captureBodies[1]);
        Assert.Equal(
            firstCapture.RootElement.GetProperty("idempotencyKey").GetGuid(),
            secondCapture.RootElement.GetProperty("idempotencyKey").GetGuid()
        );
        using JsonDocument body = JsonDocument.Parse(createBody!);
        Assert.Equal("CustomerQr", body.RootElement.GetProperty("source").GetString());
        Assert.Equal("Immediate", body.RootElement.GetProperty("paymentTiming").GetString());
        JsonElement lines = body.RootElement.GetProperty("lines");
        Assert.Equal(1, lines.GetArrayLength());
        JsonElement line = lines[0];
        Assert.Equal(productId, line.GetProperty("productId").GetGuid());
        Assert.Equal(1, line.GetProperty("quantity").GetInt32());

        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task Customer_qr_order_creation_rejects_a_missing_dining_session()
    {
        Guid restaurantId = Guid.NewGuid();
        CustomerQrApi api = new(new HttpClient(new EmptyHandler()));
        QrSessionEnvelope context = new(
            new QrTableResponse(Guid.NewGuid(), restaurantId, "T1", "Mesa 1"),
            null,
            "customer-token",
            true
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                api.CreateOrderAsync(
                    context,
                    new Dictionary<Guid, int>(),
                    new Dictionary<Guid, string>()
                )
        );

        Assert.Equal("Confirma el pedido para abrir una sesión.", exception.Message);
    }

    [Fact]
    public void Customer_qr_shows_network_guidance_when_preview_is_forbidden()
    {
        Services.AddSingleton(
            new CustomerQrApi(
                new HttpClient(
                    new ReplyHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = JsonContent.Create(new { title = "Red no autorizada" }),
                    })
                )
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<QrOrder> cut = Render<QrOrder>(parameters =>
            parameters.Add(component => component.QrCode, "MESA-403")
        );

        cut.WaitForAssertion(() =>
            Assert.Contains("Conéctate a la red autorizada del restaurante", cut.Markup)
        );
    }

    [Fact]
    public async Task Closed_restaurant_does_not_load_the_menu_or_persist_orders()
    {
        Guid restaurantId = Guid.NewGuid();
        List<string> requests = [];
        QrSessionEnvelope closed = new(
            new QrTableResponse(Guid.NewGuid(), restaurantId, "MESA-C", "Mesa cerrada"),
            null,
            "customer-token",
            false
        );
        Services.AddSingleton(
            new CustomerQrApi(
                new HttpClient(
                    new ReplyHandler(request =>
                    {
                        requests.Add(request.RequestUri!.AbsolutePath);
                        return JsonResponse(closed);
                    })
                )
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<QrOrder> cut = Render<QrOrder>(parameters =>
            parameters.Add(component => component.QrCode, "MESA-C")
        );
        cut.WaitForAssertion(() => Assert.Contains("El local aún no está disponible", cut.Markup));

        Assert.Single(requests);
        Assert.Equal(string.Empty, GetField(cut.Instance, "ordersStorageKey"));
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task Guest_count_is_validated_before_opening_a_session()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        List<string> requests = [];
        QrSessionEnvelope envelope = new(
            new QrTableResponse(tableId, restaurantId, "MESA-G", "Mesa", true),
            new QrDiningSessionResponse(sessionId, restaurantId, tableId, "Open", true),
            "customer-token",
            true
        );
        Services.AddSingleton(
            new CustomerQrApi(
                new HttpClient(
                    new ReplyHandler(request =>
                    {
                        string path = request.RequestUri!.AbsolutePath;
                        requests.Add($"{request.Method} {request.RequestUri.PathAndQuery}");
                        if (path == "/api/dining/qr/MESA-G")
                            return JsonResponse(envelope);
                        if (path == $"/api/restaurant-operations/restaurants/{restaurantId}")
                            return JsonResponse(
                                new RestaurantResponse(
                                    restaurantId,
                                    "R-G",
                                    "Restaurante",
                                    "Calle",
                                    true,
                                    1,
                                    DateTime.UtcNow
                                )
                            );
                        if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
                            return JsonResponse(
                                new[]
                                {
                                    new MenuItemResponse(
                                        restaurantId,
                                        productId,
                                        "SKU-G",
                                        "Producto",
                                        Guid.NewGuid(),
                                        "COMIDA",
                                        "Comida",
                                        10m,
                                        true,
                                        "COCINA",
                                        "Cocina"
                                    ),
                                }
                            );
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    })
                )
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<QrOrder> cut = Render<QrOrder>(parameters =>
            parameters.Add(component => component.QrCode, "MESA-G")
        );
        cut.WaitForAssertion(() => Assert.Contains("Producto", cut.Markup));
        SetField(cut.Instance, "quantities", new Dictionary<Guid, int> { [productId] = 1 });
        await cut.Find(".checkout-bar button").ClickAsync();

        Assert.Contains("Indica entre 1 y 999 comensales", cut.Markup);
        Assert.DoesNotContain(requests, request => request.Contains("/sessions"));
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task Guest_count_is_saved_before_customer_qr_order_is_created()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        List<string> requests = [];
        QrSessionEnvelope preview = new(
            new QrTableResponse(tableId, restaurantId, "MESA-N", "Mesa nueva", true),
            null,
            "customer-token",
            true
        );
        QrSessionEnvelope opened = preview with
        {
            Session = new QrDiningSessionResponse(sessionId, restaurantId, tableId, "Open", true),
        };
        Services.AddSingleton(
            new CustomerQrApi(
                new HttpClient(
                    new ReplyHandler(request =>
                    {
                        string path = request.RequestUri!.AbsolutePath;
                        requests.Add($"{request.Method} {request.RequestUri.PathAndQuery}");
                        if (path == "/api/dining/qr/MESA-N")
                            return JsonResponse(preview);
                        if (path == "/api/dining/qr/MESA-N/sessions")
                            return JsonResponse(opened);
                        if (path == $"/api/dining/sessions/{sessionId}/guests")
                            return JsonResponse(
                                new QrDiningSessionResponse(
                                    sessionId,
                                    restaurantId,
                                    tableId,
                                    "Open",
                                    true,
                                    3
                                )
                            );
                        if (path == $"/api/restaurant-operations/restaurants/{restaurantId}")
                            return JsonResponse(
                                new RestaurantResponse(
                                    restaurantId,
                                    "R-N",
                                    "Restaurante",
                                    "Calle",
                                    true,
                                    1,
                                    DateTime.UtcNow
                                )
                            );
                        if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
                            return JsonResponse(
                                new[]
                                {
                                    new MenuItemResponse(
                                        restaurantId,
                                        productId,
                                        "SKU-N",
                                        "Producto",
                                        Guid.NewGuid(),
                                        "COMIDA",
                                        "Comida",
                                        10m,
                                        true,
                                        "COCINA",
                                        "Cocina"
                                    ),
                                }
                            );
                        if (path == "/api/orders")
                            return JsonResponse(
                                new OrderResponse(
                                    orderId,
                                    restaurantId,
                                    "Draft",
                                    10m,
                                    1,
                                    DateTime.UtcNow
                                )
                            );
                        if (path == $"/api/payments/orders/{orderId}/capture-customer-qr")
                            return JsonResponse(
                                new PaymentResponse(
                                    Guid.NewGuid(),
                                    orderId,
                                    restaurantId,
                                    10m,
                                    "CustomerQr",
                                    "Paid",
                                    DateTime.UtcNow,
                                    null
                                )
                            );
                        if (path == $"/api/orders/{orderId}/submit")
                            return JsonResponse(
                                new OrderResponse(
                                    orderId,
                                    restaurantId,
                                    "Submitted",
                                    10m,
                                    2,
                                    DateTime.UtcNow
                                )
                            );
                        if (path == $"/api/payments/restaurants/{restaurantId}/order-statuses")
                            return JsonResponse(Array.Empty<object>());
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    })
                )
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<QrOrder> cut = Render<QrOrder>(parameters =>
            parameters.Add(component => component.QrCode, "MESA-N")
        );
        cut.WaitForAssertion(() => Assert.Contains("Producto", cut.Markup));
        await cut.Find("input[placeholder='Cantidad de comensales']").ChangeAsync("3");
        await cut.Find(".quantity-control button:last-child").ClickAsync();
        await cut.Find(".checkout-bar button").ClickAsync();

        Assert.Contains("POST /api/dining/qr/MESA-N/sessions?guestCount=3", requests);
        Assert.Contains($"PUT /api/dining/sessions/{sessionId}/guests", requests);
        Assert.Contains("POST /api/orders", requests);
        Assert.Contains("pagado y enviado", cut.Markup);
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task Retry_after_capture_failure_reuses_order_and_idempotency_key()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        int orderCreations = 0;
        List<string> captureBodies = [];
        QrSessionEnvelope envelope = new(
            new QrTableResponse(tableId, restaurantId, "MESA-R", "Mesa", false),
            new QrDiningSessionResponse(sessionId, restaurantId, tableId, "Open", false, 2),
            "customer-token",
            true
        );
        Services.AddSingleton(
            new CustomerQrApi(
                new HttpClient(
                    new ReplyHandler(request =>
                    {
                        string path = request.RequestUri!.AbsolutePath;
                        if (path == "/api/dining/qr/MESA-R" || path.EndsWith("/sessions"))
                            return JsonResponse(envelope);
                        if (path == $"/api/restaurant-operations/restaurants/{restaurantId}")
                            return JsonResponse(
                                new RestaurantResponse(
                                    restaurantId,
                                    "R-R",
                                    "Restaurante",
                                    "Calle",
                                    true,
                                    1,
                                    DateTime.UtcNow
                                )
                            );
                        if (path == $"/api/catalog/restaurants/{restaurantId}/menu")
                            return JsonResponse(
                                new[]
                                {
                                    new MenuItemResponse(
                                        restaurantId,
                                        productId,
                                        "SKU-R",
                                        "Producto",
                                        Guid.NewGuid(),
                                        "COMIDA",
                                        "Comida",
                                        10m,
                                        true,
                                        "COCINA",
                                        "Cocina"
                                    ),
                                }
                            );
                        if (path == "/api/orders" && request.Method == HttpMethod.Post)
                        {
                            orderCreations++;
                            return JsonResponse(
                                new OrderResponse(
                                    orderId,
                                    restaurantId,
                                    "Draft",
                                    10m,
                                    1,
                                    DateTime.UtcNow
                                )
                            );
                        }
                        if (path == $"/api/payments/orders/{orderId}/capture-customer-qr")
                        {
                            captureBodies.Add(
                                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()
                            );
                            return captureBodies.Count == 1
                                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                                {
                                    Content = JsonContent.Create(
                                        new { title = "Pago temporalmente no disponible" }
                                    ),
                                }
                                : JsonResponse(
                                    new PaymentResponse(
                                        Guid.NewGuid(),
                                        orderId,
                                        restaurantId,
                                        10m,
                                        "CustomerQr",
                                        "Paid",
                                        DateTime.UtcNow,
                                        null
                                    )
                                );
                        }
                        if (path == $"/api/orders/{orderId}/submit")
                            return JsonResponse(
                                new OrderResponse(
                                    orderId,
                                    restaurantId,
                                    "Submitted",
                                    10m,
                                    2,
                                    DateTime.UtcNow
                                )
                            );
                        if (path == $"/api/payments/restaurants/{restaurantId}/order-statuses")
                            return JsonResponse(Array.Empty<object>());
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    })
                )
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<QrOrder> cut = Render<QrOrder>(parameters =>
            parameters.Add(component => component.QrCode, "MESA-R")
        );
        cut.WaitForAssertion(() => Assert.Contains("Producto", cut.Markup));
        await cut.Find(".quantity-control button:last-child").ClickAsync();
        await cut.Find(".checkout-bar button").ClickAsync();

        Assert.Contains("Pago temporalmente no disponible", cut.Markup);
        Assert.Equal(orderId, ((OrderResponse)GetField(cut.Instance, "pendingOrder")!).Id);

        await cut.Find(".primary-button").ClickAsync();
        cut.WaitForAssertion(() => Assert.Contains("Producto", cut.Markup));
        await cut.Find(".checkout-bar button").ClickAsync();

        Assert.Equal(1, orderCreations);
        Assert.Equal(2, captureBodies.Count);
        using JsonDocument first = JsonDocument.Parse(captureBodies[0]);
        using JsonDocument second = JsonDocument.Parse(captureBodies[1]);
        Assert.Equal(
            first.RootElement.GetProperty("idempotencyKey").GetGuid(),
            second.RootElement.GetProperty("idempotencyKey").GetGuid()
        );
        Assert.Contains("pagado y enviado", cut.Markup);
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task Changing_the_qr_replaces_the_previous_status_polling_cycle()
    {
        Services.AddSingleton(
            new CustomerQrApi(
                new HttpClient(new CustomerQrHandler())
                {
                    BaseAddress = new Uri("https://test.local"),
                }
            )
        );
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<QrOrder> cut = Render<QrOrder>(parameters =>
            parameters.Add(component => component.QrCode, "MESA-1")
        );
        using CancellationTokenSource previousPolling = new();
        SetField(cut.Instance, "statusPolling", previousPolling);

        await cut.InvokeAsync(() =>
            cut.Instance.SetParametersAsync(
                ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(QrOrder.QrCode)] = "MESA-2" }
                )
            )
        );

        Assert.Contains("Mesa 2", cut.Markup);
        Assert.True(previousPolling.IsCancellationRequested);
    }

    [Fact]
    public async Task Commander_disposal_awaits_the_active_refresh_cancellation()
    {
        HttpClient http = new(new EmptyHandler()) { BaseAddress = new Uri("https://test.local") };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        using CancellationTokenSource realtimeRefresh = new();
        SetField(cut.Instance, "realtimeRefresh", realtimeRefresh);

        await cut.Instance.DisposeAsync();

        Assert.True(realtimeRefresh.IsCancellationRequested);
    }

    [Fact]
    public async Task Pos_disposal_awaits_both_active_cancellations()
    {
        HttpClient http = new(new EmptyHandler()) { BaseAddress = new Uri("https://test.local") };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        using CancellationTokenSource realtimeRefresh = new();
        using CancellationTokenSource reconciliation = new();
        SetField(cut.Instance, "realtimeRefresh", realtimeRefresh);
        SetField(cut.Instance, "reconciliation", reconciliation);

        await cut.Instance.DisposeAsync();

        Assert.True(realtimeRefresh.IsCancellationRequested);
        Assert.True(reconciliation.IsCancellationRequested);
    }

    [Fact]
    public void Commander_session_order_state_includes_active_orders_and_local_submission()
    {
        CommanderHome home = new();
        Guid sessionId = Guid.NewGuid();
        SetField(home, "sessionId", sessionId);
        SetField(
            home,
            "activeOrders",
            new List<CommanderOrder>
            {
                new(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    null,
                    sessionId,
                    "Cliente",
                    "DineIn",
                    "AfterService",
                    "Submitted",
                    12m,
                    DateTime.UtcNow
                ),
            }
        );

        PropertyInfo state = typeof(CommanderHome).GetProperty(
            "HasSessionOrders",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.True((bool)state.GetValue(home)!);

        SetField(home, "activeOrders", new List<CommanderOrder>());
        Assert.False((bool)state.GetValue(home)!);

        state.SetValue(home, true);
        Assert.True((bool)state.GetValue(home)!);
    }

    [Fact]
    public async Task Commander_selects_a_table_submits_an_order_and_clears_the_session()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        CommanderTable table = new(
            tableId,
            restaurantId,
            "T1",
            "Mesa 1",
            true,
            "Occupied",
            sessionId,
            Guid.NewGuid(),
            "Sala",
            1
        );
        CommanderSession session = new(sessionId, restaurantId, tableId, "Open");
        CommanderMenuItem product = new(
            restaurantId,
            productId,
            "P1",
            "Producto",
            Guid.NewGuid(),
            "FOOD",
            "Comida",
            12m,
            true,
            "KITCHEN",
            "Cocina"
        );
        CommanderOrder order = new(
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
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add($"{request.Method} {path}");
                return request.Method == HttpMethod.Get
                    && path == $"/api/dining/tables/{tableId}/active-session"
                        ? JsonResponse(session)
                    : request.Method == HttpMethod.Get
                    && path == $"/api/catalog/restaurants/{restaurantId}/menu"
                        ? JsonResponse(new[] { product })
                    : request.Method == HttpMethod.Post
                    && (path == "/api/orders" || path == $"/api/orders/{orderId}/submit")
                        ? JsonResponse(order)
                    : request.Method == HttpMethod.Get && path == "/api/orders/commander"
                        ? JsonResponse(new[] { order })
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        SetField(cut.Instance, "restaurantId", restaurantId);
        MethodInfo selectTable = typeof(CommanderHome).GetMethod(
            "SelectTable",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo changeQuantity = typeof(CommanderHome).GetMethod(
            "ChangeQuantity",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo sendOrder = typeof(CommanderHome).GetMethod(
            "SendOrder",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo backToTables = typeof(CommanderHome).GetMethod(
            "BackToTables",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        PropertyInfo hasSessionOrders = typeof(CommanderHome).GetProperty(
            "HasSessionOrders",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        PropertyInfo itemCount = typeof(CommanderHome).GetProperty(
            "ItemCount",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)selectTable.Invoke(cut.Instance, [table])!);
        Assert.Contains($"GET /api/dining/tables/{tableId}/active-session", requests);
        Assert.Contains($"GET /api/catalog/restaurants/{restaurantId}/menu", requests);
        Assert.Equal(sessionId, (Guid)GetField(cut.Instance, "sessionId")!);
        Assert.False((bool)hasSessionOrders.GetValue(cut.Instance)!);

        await cut.InvokeAsync(() => changeQuantity.Invoke(cut.Instance, [productId, 1]));
        await cut.InvokeAsync(() => (Task)sendOrder.Invoke(cut.Instance, null)!);
        Assert.True((bool)hasSessionOrders.GetValue(cut.Instance)!);
        Assert.Contains("enviado a cocina", (string)GetField(cut.Instance, "message")!);
        Assert.Contains("POST /api/orders", requests);
        Assert.Contains($"POST /api/orders/{orderId}/submit", requests);
        Assert.Equal(0, (int)itemCount.GetValue(cut.Instance)!);

        await cut.InvokeAsync(() => backToTables.Invoke(cut.Instance, null));
        Assert.False((bool)hasSessionOrders.GetValue(cut.Instance)!);
        Assert.Null(GetField(cut.Instance, "selectedTable"));
        Assert.Equal(Guid.Empty, (Guid)GetField(cut.Instance, "sessionId")!);
    }

    [Fact]
    public async Task Commander_requires_guest_count_before_loading_the_menu_for_a_new_session()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        CommanderTable table = new(
            tableId,
            restaurantId,
            "T1",
            "Mesa 1",
            true,
            "Free",
            null,
            Guid.NewGuid(),
            "Sala",
            1
        );
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add($"{request.Method} {request.RequestUri.PathAndQuery}");
                if (
                    request.Method == HttpMethod.Post
                    && path == $"/api/dining/tables/{tableId}/sessions"
                )
                {
                    return JsonResponse(
                        new CommanderSession(
                            sessionId,
                            restaurantId,
                            tableId,
                            "Open",
                            RequestGuestCount: true
                        )
                    );
                }

                if (
                    request.Method == HttpMethod.Put
                    && path == $"/api/dining/sessions/{sessionId}/guests"
                )
                {
                    return JsonResponse(
                        new CommanderSession(
                            sessionId,
                            restaurantId,
                            tableId,
                            "Open",
                            RequestGuestCount: true,
                            GuestCount: 4
                        )
                    );
                }

                return
                    request.Method == HttpMethod.Get
                    && path == $"/api/catalog/restaurants/{restaurantId}/menu"
                    ? JsonResponse(Array.Empty<CommanderMenuItem>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        SetField(cut.Instance, "restaurantId", restaurantId);
        MethodInfo selectTable = typeof(CommanderHome).GetMethod(
            "SelectTable",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo acceptGuestCount = typeof(CommanderHome).GetMethod(
            "AcceptGuestCount",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)selectTable.Invoke(cut.Instance, [table])!);

        Assert.True((bool)GetField(cut.Instance, "guestCountDialogOpen")!);
        Assert.Null(GetField(cut.Instance, "selectedTable"));
        Assert.DoesNotContain(
            requests,
            request => request.Contains("/menu", StringComparison.Ordinal)
        );

        SetField(cut.Instance, "guestCountDraft", 0);
        await cut.InvokeAsync(() => (Task)acceptGuestCount.Invoke(cut.Instance, null)!);
        Assert.DoesNotContain(
            requests,
            request => request.Contains("/guests", StringComparison.Ordinal)
        );
        Assert.True((bool)GetField(cut.Instance, "guestCountDialogOpen")!);

        SetField(cut.Instance, "guestCountDraft", 4);
        await cut.InvokeAsync(() => (Task)acceptGuestCount.Invoke(cut.Instance, null)!);

        Assert.Equal(4, GetField(cut.Instance, "guestCount"));
        Assert.Equal(table, GetField(cut.Instance, "selectedTable"));
        Assert.False((bool)GetField(cut.Instance, "guestCountDialogOpen")!);
        Assert.Contains($"PUT /api/dining/sessions/{sessionId}/guests", requests);
        Assert.Contains(
            $"GET /api/catalog/restaurants/{restaurantId}/menu?channel=Commander",
            requests
        );
    }

    [Fact]
    public async Task Commander_cancels_a_new_session_when_guest_count_is_abandoned()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid tableId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        CommanderTable table = new(
            tableId,
            restaurantId,
            "T1",
            "Mesa 1",
            true,
            "Free",
            null,
            Guid.NewGuid(),
            "Sala",
            1
        );
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add($"{request.Method} {path}");
                return path == $"/api/dining/tables/{tableId}/sessions"
                        ? JsonResponse(
                            new CommanderSession(
                                sessionId,
                                restaurantId,
                                tableId,
                                "Open",
                                RequestGuestCount: true
                            )
                        )
                    : path == $"/api/dining/sessions/{sessionId}/cancel"
                        ? JsonResponse(
                            new CommanderSession(sessionId, restaurantId, tableId, "Cancelled")
                        )
                    : path == $"/api/dining/restaurants/{restaurantId}/tables"
                        ? JsonResponse(new[] { table })
                    : path == $"/api/orders/commander?restaurantId={restaurantId}"
                        ? JsonResponse(Array.Empty<CommanderOrder>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        SetField(cut.Instance, "restaurantId", restaurantId);
        MethodInfo selectTable = typeof(CommanderHome).GetMethod(
            "SelectTable",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo cancelGuestCount = typeof(CommanderHome).GetMethod(
            "CancelGuestCount",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)selectTable.Invoke(cut.Instance, [table])!);
        await cut.InvokeAsync(() => (Task)cancelGuestCount.Invoke(cut.Instance, null)!);

        Assert.Contains($"POST /api/dining/sessions/{sessionId}/cancel", requests);
        Assert.Contains($"GET /api/dining/restaurants/{restaurantId}/tables", requests);
        Assert.Null(GetField(cut.Instance, "selectedTable"));
        Assert.Null(GetField(cut.Instance, "diningSession"));
        Assert.False((bool)GetField(cut.Instance, "guestCountDialogOpen")!);
    }

    [Fact]
    public async Task Commander_ignores_unavailable_restaurants_and_loads_the_selected_restaurant()
    {
        Guid firstRestaurantId = Guid.NewGuid();
        Guid selectedRestaurantId = Guid.NewGuid();
        Guid unauthorizedRestaurantId = Guid.NewGuid();
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add(path);
                return path == $"/api/dining/restaurants/{selectedRestaurantId}/tables"
                        ? JsonResponse(Array.Empty<CommanderTable>())
                    : path == $"/api/orders/commander?restaurantId={selectedRestaurantId}"
                        ? JsonResponse(Array.Empty<CommanderOrder>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        requests.Clear();
        SetField(cut.Instance, "restaurantId", firstRestaurantId);
        SetField(
            cut.Instance,
            "restaurants",
            new List<Restaurantes.Clients.Commander.Pwa.Models.RestaurantResponse>
            {
                new(firstRestaurantId, "A", "Primero", "", true),
                new(selectedRestaurantId, "B", "Segundo", "", true),
            }
        );
        MethodInfo restaurantChanged = typeof(CommanderHome).GetMethod(
            "RestaurantChanged",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() =>
            (Task)
                restaurantChanged.Invoke(
                    cut.Instance,
                    [new ChangeEventArgs { Value = unauthorizedRestaurantId }]
                )!
        );

        Assert.Equal(firstRestaurantId, GetField(cut.Instance, "restaurantId"));
        Assert.Empty(requests);

        await cut.InvokeAsync(() =>
            (Task)
                restaurantChanged.Invoke(
                    cut.Instance,
                    [new ChangeEventArgs { Value = selectedRestaurantId }]
                )!
        );

        Assert.Equal(selectedRestaurantId, GetField(cut.Instance, "restaurantId"));
        Assert.Contains($"/api/dining/restaurants/{selectedRestaurantId}/tables", requests);
        Assert.Contains($"/api/orders/commander?restaurantId={selectedRestaurantId}", requests);
        Assert.DoesNotContain(
            requests,
            request => request.Contains(firstRestaurantId.ToString(), StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Commander_cart_quantity_changes_respect_product_and_quantity_boundaries()
    {
        Guid productId = Guid.NewGuid();
        Guid unknownProductId = Guid.NewGuid();
        CommanderMenuItem product = new(
            Guid.NewGuid(),
            productId,
            "P1",
            "Producto",
            Guid.NewGuid(),
            "FOOD",
            "Comida",
            5m,
            true,
            "KITCHEN",
            "Cocina"
        );
        CommanderHome home = new();
        SetField(home, "menu", new List<CommanderMenuItem> { product });
        MethodInfo changeQuantity = typeof(CommanderHome).GetMethod(
            "ChangeQuantity",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        PropertyInfo itemCount = typeof(CommanderHome).GetProperty(
            "ItemCount",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        changeQuantity.Invoke(home, [unknownProductId, 1]);
        Assert.Equal(0, (int)itemCount.GetValue(home)!);

        changeQuantity.Invoke(home, [productId, -1]);
        Assert.Equal(0, (int)itemCount.GetValue(home)!);

        changeQuantity.Invoke(home, [productId, 1]);
        changeQuantity.Invoke(home, [productId, 200]);
        Assert.Equal(99, (int)itemCount.GetValue(home)!);

        changeQuantity.Invoke(home, [productId, -200]);
        Assert.Equal(0, (int)itemCount.GetValue(home)!);
    }

    [Fact]
    public async Task Commander_loads_the_first_accessible_restaurant_when_the_current_one_is_unavailable()
    {
        Guid currentRestaurantId = Guid.NewGuid();
        Guid accessibleRestaurantId = Guid.NewGuid();
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/api/restaurant-operations/restaurants/accessible/commander")
                {
                    return JsonResponse(
                        new[]
                        {
                            new Restaurantes.Clients.Commander.Pwa.Models.RestaurantResponse(
                                accessibleRestaurantId,
                                "LOCAL",
                                "Local accesible",
                                "",
                                true
                            ),
                        }
                    );
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        SetField(cut.Instance, "restaurantId", currentRestaurantId);
        MethodInfo loadRestaurants = typeof(CommanderHome).GetMethod(
            "LoadRestaurants",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)loadRestaurants.Invoke(cut.Instance, null)!);

        Assert.Equal(accessibleRestaurantId, GetField(cut.Instance, "restaurantId"));
        Assert.Single(
            (List<Restaurantes.Clients.Commander.Pwa.Models.RestaurantResponse>)
                GetField(cut.Instance, "restaurants")!
        );
    }

    [Fact]
    public async Task Commander_rejects_loading_when_no_restaurant_is_accessible()
    {
        HttpClient http = new(
            new ReplyHandler(request =>
                request.RequestUri!.AbsolutePath
                == "/api/restaurant-operations/restaurants/accessible/commander"
                    ? JsonResponse(
                        Array.Empty<Restaurantes.Clients.Commander.Pwa.Models.RestaurantResponse>()
                    )
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
            )
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        MethodInfo loadRestaurants = typeof(CommanderHome).GetMethod(
            "LoadRestaurants",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await cut.InvokeAsync(() => (Task)loadRestaurants.Invoke(cut.Instance, null)!)
        );

        Assert.Contains("no tiene permisos", exception.Message);
    }

    [Fact]
    public async Task Commander_releases_a_paid_session_only_for_its_own_commander_order()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.PathAndQuery;
                requests.Add($"{request.Method} {path}");
                return path == $"/api/dining/sessions/{sessionId}/close"
                        ? JsonResponse(
                            new CommanderSession(sessionId, restaurantId, Guid.NewGuid(), "Closed")
                        )
                    : path == $"/api/dining/restaurants/{restaurantId}/tables"
                        ? JsonResponse(Array.Empty<CommanderTable>())
                    : path == $"/api/orders/commander?restaurantId={restaurantId}"
                        ? JsonResponse(Array.Empty<CommanderOrder>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(
            cut.Instance,
            "login",
            new CommanderLogin(
                "token",
                DateTime.UtcNow.AddHours(1),
                new(userId, "user", "Usuario"),
                []
            )
        );
        SetField(cut.Instance, "sessionId", sessionId);
        SetField(
            cut.Instance,
            "diningSession",
            new CommanderSession(sessionId, restaurantId, Guid.NewGuid(), "Open")
        );
        SetField(
            cut.Instance,
            "bill",
            new CommanderBill(
                sessionId,
                restaurantId,
                Guid.NewGuid(),
                "Open",
                20m,
                0m,
                [
                    new CommanderBillOrder(
                        orderId,
                        "Submitted",
                        "Paid",
                        20m,
                        20m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ],
                CanRelease: true
            )
        );
        MethodInfo releasePaidSession = typeof(CommanderHome).GetMethod(
            "ReleasePaidSession",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)releasePaidSession.Invoke(cut.Instance, null)!);

        Assert.Contains($"POST /api/dining/sessions/{sessionId}/close", requests);
        Assert.Contains($"GET /api/dining/restaurants/{restaurantId}/tables", requests);
        Assert.True((bool)GetField(cut.Instance, "success")!);
        Assert.Equal("Mesa liberada.", GetField(cut.Instance, "message"));
        Assert.Null(GetField(cut.Instance, "diningSession"));
    }

    [Fact]
    public void Commander_checkout_allocations_cover_only_own_unpaid_orders_in_order()
    {
        Guid userId = Guid.NewGuid();
        Guid firstOrderId = Guid.NewGuid();
        Guid secondOrderId = Guid.NewGuid();
        Guid otherOrderId = Guid.NewGuid();
        CommanderHome home = new();
        SetField(
            home,
            "login",
            new CommanderLogin(
                "token",
                DateTime.UtcNow.AddHours(1),
                new(userId, "user", "Usuario"),
                []
            )
        );
        SetField(
            home,
            "bill",
            new CommanderBill(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Open",
                55m,
                35m,
                [
                    new(
                        firstOrderId,
                        "Submitted",
                        "Partial",
                        12m,
                        2m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                    new(
                        otherOrderId,
                        "Submitted",
                        "Unpaid",
                        10m,
                        0m,
                        DateTime.UtcNow,
                        Source: "Pos",
                        CreatedByUserId: userId
                    ),
                    new(
                        secondOrderId,
                        "Submitted",
                        "Unpaid",
                        30m,
                        5m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                    new(
                        Guid.NewGuid(),
                        "Cancelled",
                        "Unpaid",
                        8m,
                        0m,
                        DateTime.UtcNow,
                        Source: "Comandero",
                        CreatedByUserId: userId
                    ),
                ]
            )
        );
        SetField(home, "currentChargeAmount", 20m);
        MethodInfo getCurrentAllocations = typeof(CommanderHome).GetMethod(
            "GetCurrentAllocations",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        List<CommanderApi.CheckoutOrderAllocation> allocations =
            (List<CommanderApi.CheckoutOrderAllocation>)getCurrentAllocations.Invoke(home, null)!;

        Assert.Collection(
            allocations,
            allocation =>
            {
                Assert.Equal(firstOrderId, allocation.OrderId);
                Assert.Equal(10m, allocation.Amount);
            },
            allocation =>
            {
                Assert.Equal(secondOrderId, allocation.OrderId);
                Assert.Equal(10m, allocation.Amount);
            }
        );
    }

    [Fact]
    public void Commander_payment_keypad_enforces_precision_and_amount_limits()
    {
        CommanderHome home = new();
        SetField(home, "paymentKeypadStartNew", true);
        MethodInfo buildPaymentValue = typeof(CommanderHome).GetMethod(
            "TryBuildPaymentKeypadValue",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        object?[] arguments = ["1", null, 0m];

        Assert.True((bool)buildPaymentValue.Invoke(home, arguments)!);
        Assert.Equal("1", arguments[1]);
        Assert.Equal(1m, arguments[2]);

        SetField(home, "paymentKeypadStartNew", false);
        SetField(home, "paymentKeypadText", "12,34");
        arguments = ["5", null, 0m];
        Assert.False((bool)buildPaymentValue.Invoke(home, arguments)!);

        SetField(home, "paymentKeypadText", "1000000");
        arguments = ["1", null, 0m];
        Assert.False((bool)buildPaymentValue.Invoke(home, arguments)!);

        arguments = ["Borrar", null, 0m];
        Assert.True((bool)buildPaymentValue.Invoke(home, arguments)!);
        Assert.Equal("0", arguments[1]);
        Assert.Equal(0m, arguments[2]);
    }

    [Fact]
    public async Task Pos_logout_cancels_reconciliation_and_clears_session()
    {
        HttpClient http = new(new EmptyHandler()) { BaseAddress = new Uri("https://test.local") };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        using CancellationTokenSource reconciliation = new();
        SetField(cut.Instance, "reconciliation", reconciliation);

        MethodInfo logout = typeof(PosHome).GetMethod(
            "Logout",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await cut.InvokeAsync(() => (Task)logout.Invoke(cut.Instance, null)!);

        Assert.True(reconciliation.IsCancellationRequested);
        Assert.Null(GetField(cut.Instance, "reconciliation"));
        Assert.Null(api.AccessToken);
    }

    [Fact]
    public async Task Commander_cancelled_line_refreshes_bill_and_shows_success_notice()
    {
        Guid sessionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                requests.Add(request.RequestUri!.AbsolutePath);
                string path = request.RequestUri.AbsolutePath;
                return path == $"/api/orders/{orderId}/lines/{lineId}/cancel"
                        ? JsonResponse(
                            new CommanderOrderDetail(orderId, sessionId, "Submitted", 12m, [], [])
                        )
                    : path == $"/api/dining/sessions/{sessionId}/bill"
                        ? JsonResponse(
                            new CommanderBill(
                                sessionId,
                                Guid.NewGuid(),
                                Guid.NewGuid(),
                                "Open",
                                0m,
                                0m,
                                []
                            )
                        )
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        CommanderApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new CommanderOrderRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        IRenderedComponent<CommanderHome> cut = Render<CommanderHome>();
        SetField(cut.Instance, "sessionId", sessionId);
        using CancellationTokenSource refresh = new();
        SetField(cut.Instance, "realtimeRefresh", refresh);
        CommanderBillOrder order = new(orderId, "Submitted", "Unpaid", 12m, 0m, DateTime.UtcNow);
        CommanderOrderLine line = new(
            lineId,
            "Producto",
            12m,
            1,
            12m,
            "KITCHEN",
            "Cocina",
            "Active",
            null,
            ""
        );
        MethodInfo cancel = typeof(CommanderHome).GetMethod(
            "CancelLine",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)cancel.Invoke(cut.Instance, [order, line])!);

        Assert.Contains($"/api/orders/{orderId}/lines/{lineId}/cancel", requests);
        Assert.Contains($"/api/dining/sessions/{sessionId}/bill", requests);
        Assert.Equal("Producto cancelado antes de preparación.", GetField(cut.Instance, "message"));
        Assert.True((bool)GetField(cut.Instance, "success")!);
    }

    [Fact]
    public async Task Pos_pickup_delivery_refreshes_orders_after_payment()
    {
        Guid orderId = Guid.NewGuid();
        PosOrder pickup = new(
            orderId,
            Guid.NewGuid(),
            null,
            null,
            "Cliente",
            "Takeaway",
            "Immediate",
            "Ready",
            12m,
            DateTime.UtcNow
        );
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add(path);
                return path == $"/api/orders/{orderId}/deliver" ? JsonResponse(pickup)
                    : path == "/api/orders/pos" && request.Method == HttpMethod.Get
                        ? JsonResponse(Array.Empty<PosOrder>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        SetField(cut.Instance, "selectedPickup", pickup);
        SetField(cut.Instance, "pickupPaymentStatus", "Paid");
        MethodInfo deliver = typeof(PosHome).GetMethod(
            "DeliverPickup",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)deliver.Invoke(cut.Instance, null)!);

        Assert.Contains($"/api/orders/{orderId}/deliver", requests);
        Assert.Contains("/api/orders/pos", requests);
        Assert.Contains("entregado", (string)GetField(cut.Instance, "message")!);
    }

    [Fact]
    public async Task Pos_cancelled_line_reports_success_after_refresh_delay()
    {
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                requests.Add(request.RequestUri!.AbsolutePath);
                return request.RequestUri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal)
                    ? JsonResponse(
                        new PosOrderDetail(orderId, Guid.NewGuid(), "Submitted", 12m, [], [])
                    )
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        using CancellationTokenSource refresh = new();
        SetField(cut.Instance, "realtimeRefresh", refresh);
        PosBillOrder order = new(orderId, "Submitted", "Unpaid", 12m, 0m, DateTime.UtcNow);
        PosOrderLine line = new(
            lineId,
            "Producto",
            12m,
            1,
            12m,
            "KITCHEN",
            "Cocina",
            "Active",
            null,
            ""
        );
        MethodInfo cancel = typeof(PosHome).GetMethod(
            "CancelLine",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)cancel.Invoke(cut.Instance, [order, line])!);

        Assert.Contains($"/api/orders/{orderId}/lines/{lineId}/cancel", requests);
        Assert.Contains("cancelado", (string)GetField(cut.Instance, "message")!);
    }

    [Fact]
    public async Task Pos_takeaway_order_refreshes_pickups_after_submission()
    {
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        PosOrder order = new(
            orderId,
            restaurantId,
            null,
            null,
            "Cliente",
            "Takeaway",
            "OnAccount",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        List<string> requests = [];
        string? createOrderBody = null;
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add(path);
                if (
                    request.Method == HttpMethod.Post
                    && (path == "/api/orders" || path == $"/api/orders/{orderId}/submit")
                )
                {
                    if (path == "/api/orders")
                    {
                        createOrderBody = request
                            .Content!.ReadAsStringAsync()
                            .GetAwaiter()
                            .GetResult();
                    }
                    return JsonResponse(order);
                }
                return request.Method == HttpMethod.Get && path == "/api/orders/pos"
                    ? JsonResponse(Array.Empty<PosOrder>())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        PosCashRegister register = new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.UtcNow),
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
            "Tester",
            null,
            null,
            [],
            [],
            [],
            []
        );
        SetField(cut.Instance, "cashRegister", register);
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(cut.Instance, "serviceMode", "Takeaway");
        SetField(cut.Instance, "takeawayOpen", true);
        SetField(cut.Instance, "customerName", "Cliente");
        MethodInfo send = typeof(PosHome).GetMethod(
            "SendOrder",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)send.Invoke(cut.Instance, null)!);

        Assert.Contains($"/api/orders/{orderId}/submit", requests);
        Assert.Contains("Recogida", (string)GetField(cut.Instance, "message")!);
        Assert.Contains("/api/orders/pos", requests);
        using JsonDocument orderBody = JsonDocument.Parse(createOrderBody!);
        Assert.Equal(JsonValueKind.Null, orderBody.RootElement.GetProperty("tableId").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            orderBody.RootElement.GetProperty("diningSessionId").ValueKind
        );
        Assert.Equal("PARA LLEVAR", orderBody.RootElement.GetProperty("tableLabel").GetString());
        Assert.Equal("Takeaway", orderBody.RootElement.GetProperty("serviceMode").GetString());
        Assert.Equal("OnAccount", orderBody.RootElement.GetProperty("paymentTiming").GetString());
    }

    [Fact]
    public async Task Pos_pickup_dialog_moves_from_open_payment_to_paid_waiting_for_kitchen()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        PosOrder pickup = new(
            orderId,
            restaurantId,
            null,
            null,
            "Cliente",
            "Takeaway",
            "Immediate",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        PosOrderLine line = new(
            lineId,
            "Producto",
            12m,
            1,
            12m,
            "KITCHEN",
            "Cocina",
            "Active",
            null,
            ""
        );
        PosOrderDetail detail = new(orderId, restaurantId, "Submitted", 12m, [line], []);
        string paymentStatus = "Open";
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == $"/api/orders/{orderId}" && request.Method == HttpMethod.Get)
                    return JsonResponse(detail);
                if (path == $"/api/payments/restaurants/{restaurantId}/order-statuses")
                    return JsonResponse(new[] { new PosPaymentStatus(orderId, paymentStatus) });
                if (path == $"/api/payments/orders/{orderId}/capture")
                {
                    paymentStatus = "Paid";
                    return JsonResponse(new { });
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();
        IRenderedComponent<PosHome> cut = Render<PosHome>();
        SetField(
            cut.Instance,
            "login",
            new PosLogin(
                "token",
                DateTime.UtcNow.AddHours(1),
                new PosLoginUser(Guid.NewGuid(), "cajero", "Cajero"),
                []
            )
        );
        SetField(cut.Instance, "initializing", false);
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(
            cut.Instance,
            "restaurants",
            new List<PosRestaurant> { new(restaurantId, "CENTRO", "Centro", "Calle Mayor", true) }
        );
        SetField(
            cut.Instance,
            "cashRegister",
            new PosCashRegister(
                Guid.NewGuid(),
                restaurantId,
                DateOnly.FromDateTime(DateTime.UtcNow),
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
                "Tester",
                null,
                null,
                [],
                [],
                [],
                []
            )
        );
        MethodInfo open = typeof(PosHome).GetMethod(
            "OpenPickup",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.Empty));
        await cut.InvokeAsync(() => (Task)open.Invoke(cut.Instance, [pickup])!);
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.Empty));

        Assert.Contains("Producto", cut.Markup);
        Assert.Contains("Cobrar pedido", cut.Markup);
        Assert.Contains("Cancelar", cut.Markup);
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Cobrar pedido"))
            .Click();

        Assert.Equal("Paid", GetField(cut.Instance, "pickupPaymentStatus"));
        Assert.Contains("Pedido pagado. Se podrá entregar", cut.Markup);
        Assert.DoesNotContain("Entregar al cliente", cut.Markup);
    }

    [Fact]
    public async Task Pos_pickup_line_cancellation_updates_order_and_payment_status()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        PosOrder pickup = new(
            orderId,
            restaurantId,
            null,
            null,
            "Cliente",
            "Takeaway",
            "Immediate",
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        PosOrderDetail cancelled = new(orderId, restaurantId, "Submitted", 0m, [], []);
        List<string> requests = [];
        string? cancellationBody = null;
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add(path);
                if (path == $"/api/orders/{orderId}/lines/{lineId}/cancel")
                {
                    cancellationBody = request
                        .Content!.ReadAsStringAsync()
                        .GetAwaiter()
                        .GetResult();
                    return JsonResponse(cancelled);
                }
                return path == $"/api/payments/restaurants/{restaurantId}/order-statuses"
                    ? JsonResponse(new[] { new PosPaymentStatus(orderId, "Open") })
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(cut.Instance, "selectedPickup", pickup);
        SetField(cut.Instance, "pickupPaymentStatus", "Open");
        PosOrderLine line = new(
            lineId,
            "Producto",
            12m,
            1,
            12m,
            "KITCHEN",
            "Cocina",
            "Active",
            null,
            ""
        )
        {
            KitchenStatus = "Pending",
        };
        MethodInfo cancel = typeof(PosHome).GetMethod(
            "CancelPickupLine",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)cancel.Invoke(cut.Instance, [line])!);

        Assert.Contains($"/api/orders/{orderId}/lines/{lineId}/cancel", requests);
        using JsonDocument body = JsonDocument.Parse(cancellationBody!);
        Assert.Equal("Motivo de prueba", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0m, ((PosOrder)GetField(cut.Instance, "selectedPickup")!).Total);
        Assert.Equal("Open", GetField(cut.Instance, "pickupPaymentStatus"));
        Assert.Contains("cancelado", (string)GetField(cut.Instance, "message")!);
    }

    [Fact]
    public async Task Pos_pickup_capture_requires_open_register_and_refreshes_paid_status()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        PosOrder pickup = new(
            orderId,
            restaurantId,
            null,
            null,
            "Cliente",
            "Takeaway",
            "Immediate",
            "Ready",
            12m,
            DateTime.UtcNow
        );
        List<string> requests = [];
        string? captureBody = null;
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add(path);
                if (path == $"/api/payments/orders/{orderId}/capture")
                {
                    captureBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return JsonResponse(new { });
                }
                return path == $"/api/payments/restaurants/{restaurantId}/order-statuses"
                    ? JsonResponse(new[] { new PosPaymentStatus(orderId, "Paid") })
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            })
        )
        {
            BaseAddress = new Uri("https://test.local"),
        };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosCatalogRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        IRenderedComponent<PosHome> cut = Render<PosHome>();
        SetField(cut.Instance, "restaurantId", restaurantId);
        SetField(cut.Instance, "selectedPickup", pickup);
        SetField(cut.Instance, "pickupPaymentStatus", "Open");
        MethodInfo capture = typeof(PosHome).GetMethod(
            "CapturePickup",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)capture.Invoke(cut.Instance, null)!);
        Assert.DoesNotContain($"/api/payments/orders/{orderId}/capture", requests);
        Assert.Contains("abrir la caja", (string)GetField(cut.Instance, "message")!);

        PosCashRegister register = new(
            Guid.NewGuid(),
            restaurantId,
            DateOnly.FromDateTime(DateTime.UtcNow),
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
            "Tester",
            null,
            null,
            [],
            [],
            [],
            []
        );
        SetField(cut.Instance, "cashRegister", register);
        SetField(cut.Instance, "paymentMethod", "Cash");
        await cut.InvokeAsync(() => (Task)capture.Invoke(cut.Instance, null)!);

        Assert.Contains($"/api/payments/orders/{orderId}/capture", requests);
        using JsonDocument body = JsonDocument.Parse(captureBody!);
        Assert.Equal("Cash", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("Paid", GetField(cut.Instance, "pickupPaymentStatus"));
        Assert.Contains("Cobro registrado", (string)GetField(cut.Instance, "message")!);
    }

    private static void SetField(object component, string name, object value)
    {
        FieldInfo field = component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(component, value);
    }

    private static object? GetField(object component, string name)
    {
        return component
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(component);
    }

    private sealed class EmptyJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        )
        {
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class ConfirmingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            object? value = identifier switch
            {
                "prompt" => "Motivo de prueba",
                "confirm" => true,
                _ => default(TValue),
            };
            return ValueTask.FromResult((TValue)value!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        )
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }

    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(reply(request));
        }
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class CustomerQrHandler : HttpMessageHandler
    {
        private static readonly Guid RestaurantId = Guid.Parse(
            "8b7d87b1-dcf5-4d2c-9d3c-013f927ed731"
        );
        private static readonly Guid FirstTableId = Guid.Parse(
            "9a2f76c4-df84-423b-b7bd-93fca4e24c54"
        );
        private static readonly Guid SecondTableId = Guid.Parse(
            "f689351d-7f0c-4eb3-b6cf-c74a48cd819e"
        );
        private static readonly Guid FirstSessionId = Guid.Parse(
            "0bb7ec44-2909-429b-9c12-a5aab9e17cef"
        );
        private static readonly Guid SecondSessionId = Guid.Parse(
            "ae8d07e4-d5af-42c5-935c-9bc4a3b35167"
        );

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            object response =
                path == "/api/dining/qr/MESA-1" ? Envelope(FirstTableId, FirstSessionId, "Mesa 1")
                : path == "/api/dining/qr/MESA-2"
                    ? Envelope(SecondTableId, SecondSessionId, "Mesa 2")
                : path == $"/api/restaurant-operations/restaurants/{RestaurantId}"
                    ? new RestaurantResponse(
                        RestaurantId,
                        "REST-1",
                        "Restaurante de prueba",
                        "Calle de prueba",
                        true,
                        1,
                        DateTime.UnixEpoch
                    )
                : path.StartsWith("/api/catalog/restaurants/", StringComparison.Ordinal)
                    ? Array.Empty<MenuItemResponse>()
                : Array.Empty<OrderResponse>();
            string content = JsonSerializer.Serialize(response);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json"),
                }
            );
        }

        private static QrSessionEnvelope Envelope(Guid tableId, Guid sessionId, string tableLabel)
        {
            return new QrSessionEnvelope(
                new QrTableResponse(tableId, RestaurantId, tableLabel, tableLabel),
                new QrDiningSessionResponse(sessionId, RestaurantId, tableId, "Open"),
                "customer-token",
                true
            );
        }
    }

    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
