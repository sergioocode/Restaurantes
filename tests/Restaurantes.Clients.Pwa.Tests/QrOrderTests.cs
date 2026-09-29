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
using CommanderDiningRealtime = Restaurantes.Clients.Commander.Pwa.Realtime.DiningRealtimeClient;
using CommanderHome = Restaurantes.Clients.Commander.Pwa.Pages.Home;
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
using PosDiningRealtime = Restaurantes.Clients.Pos.Pwa.Realtime.DiningRealtimeClient;
using PosHome = Restaurantes.Clients.Pos.Pwa.Pages.Home;
using PosOrder = Restaurantes.Clients.Pos.Pwa.Models.OrderResponse;
using PosOrderDetail = Restaurantes.Clients.Pos.Pwa.Models.OrderDetailResponse;
using PosOrderLine = Restaurantes.Clients.Pos.Pwa.Models.OrderLineDetailResponse;
using PosOrderRealtime = Restaurantes.Clients.Pos.Pwa.Realtime.OrderRealtimeClient;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class QrOrderTests : BunitContext
{
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

        var cut = Render<QrOrder>(parameters =>
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
        Services.AddSingleton(new CommanderDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        var cut = Render<CommanderHome>();
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
        Services.AddSingleton(new PosDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        var cut = Render<PosHome>();
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
                if (
                    request.Method == HttpMethod.Get
                    && path == $"/api/dining/tables/{tableId}/active-session"
                )
                {
                    return JsonResponse(session);
                }
                if (
                    request.Method == HttpMethod.Get
                    && path == $"/api/catalog/restaurants/{restaurantId}/menu"
                )
                {
                    return JsonResponse(new[] { product });
                }
                if (
                    request.Method == HttpMethod.Post
                    && (path == "/api/orders" || path == $"/api/orders/{orderId}/submit")
                )
                {
                    return JsonResponse(order);
                }
                return request.Method == HttpMethod.Get && path == "/api/orders"
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
        Services.AddSingleton(new CommanderDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        var cut = Render<CommanderHome>();
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
        Assert.Equal(0, ((Dictionary<Guid, int>)GetField(cut.Instance, "quantities")!)[productId]);

        await cut.InvokeAsync(() => backToTables.Invoke(cut.Instance, null));
        Assert.False((bool)hasSessionOrders.GetValue(cut.Instance)!);
        Assert.Null(GetField(cut.Instance, "selectedTable"));
        Assert.Equal(Guid.Empty, (Guid)GetField(cut.Instance, "sessionId")!);
    }

    [Fact]
    public async Task Pos_logout_cancels_reconciliation_and_clears_session()
    {
        HttpClient http = new(new EmptyHandler()) { BaseAddress = new Uri("https://test.local") };
        PosApi api = new(http);
        Services.AddSingleton(api);
        Services.AddSingleton(new PosOrderRealtime(http, api));
        Services.AddSingleton(new PosDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, EmptyJsRuntime>();

        var cut = Render<PosHome>();
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
    public async Task Commander_cancelled_line_reports_success_after_refresh_delay()
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
                if (path == $"/api/orders/{orderId}/lines/{lineId}/cancel")
                {
                    return JsonResponse(
                        new CommanderOrderDetail(orderId, sessionId, "Submitted", 12m, [], [])
                    );
                }

                return path == $"/api/dining/sessions/{sessionId}/bill"
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
        Services.AddSingleton(new CommanderDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        var cut = Render<CommanderHome>();
        SetField(cut.Instance, "sessionId", sessionId);
        using CancellationTokenSource refresh = new();
        SetField(cut.Instance, "realtimeRefresh", refresh);
        CommanderBillOrder order = new(orderId, "Submitted", "Unpaid", 12m, DateTime.UtcNow);
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
        Assert.Contains("cancelado", (string)GetField(cut.Instance, "message")!);
    }

    [Fact]
    public async Task Pos_pickup_delivery_refreshes_orders_after_confirmation()
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
            "Submitted",
            12m,
            DateTime.UtcNow
        );
        List<string> requests = [];
        HttpClient http = new(
            new ReplyHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                requests.Add(path);
                if (path == $"/api/orders/{orderId}/deliver")
                {
                    return JsonResponse(pickup);
                }
                return path == "/api/orders" && request.Method == HttpMethod.Get
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
        Services.AddSingleton(new PosDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        var cut = Render<PosHome>();
        MethodInfo deliver = typeof(PosHome).GetMethod(
            "CompletePickup",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)deliver.Invoke(cut.Instance, [pickup])!);

        Assert.Contains($"/api/orders/{orderId}/deliver", requests);
        Assert.Contains("/api/orders", requests);
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
        Services.AddSingleton(new PosDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        var cut = Render<PosHome>();
        using CancellationTokenSource refresh = new();
        SetField(cut.Instance, "realtimeRefresh", refresh);
        PosBillOrder order = new(orderId, "Submitted", "Unpaid", 12m, DateTime.UtcNow);
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
                    return JsonResponse(order);
                }
                return request.Method == HttpMethod.Get && path == "/api/orders"
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
        Services.AddSingleton(new PosDiningRealtime(http, api));
        Services.AddSingleton<IJSRuntime, ConfirmingJsRuntime>();

        var cut = Render<PosHome>();
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
        SetField(cut.Instance, "customerName", "Cliente");
        SetField(cut.Instance, "takeawayRequiresPrepayment", false);
        MethodInfo send = typeof(PosHome).GetMethod(
            "SendOrder",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await cut.InvokeAsync(() => (Task)send.Invoke(cut.Instance, null)!);

        Assert.Contains($"/api/orders/{orderId}/submit", requests);
        Assert.Contains("Recogida", (string)GetField(cut.Instance, "message")!);
        Assert.Contains("/api/orders", requests);
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
        ) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(reply(request));
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
            object response;
            if (path == "/api/dining/qr/MESA-1")
            {
                response = Envelope(FirstTableId, FirstSessionId, "Mesa 1");
            }
            else if (path == "/api/dining/qr/MESA-2")
            {
                response = Envelope(SecondTableId, SecondSessionId, "Mesa 2");
            }
            else if (path == $"/api/restaurant-operations/restaurants/{RestaurantId}")
            {
                response = new RestaurantResponse(
                    RestaurantId,
                    "REST-1",
                    "Restaurante de prueba",
                    "Calle de prueba",
                    true,
                    1,
                    DateTime.UnixEpoch
                );
            }
            else if (path.StartsWith("/api/catalog/restaurants/", StringComparison.Ordinal))
            {
                response = Array.Empty<MenuItemResponse>();
            }
            else
            {
                response = Array.Empty<OrderResponse>();
            }

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
                false,
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
