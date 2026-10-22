using System.Net;
using System.Net.Http.Json;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.OrderSimulator;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class SimulatorOrderFlowTests
{
    private static readonly Guid RestaurantId = Guid.Parse("c0208764-810c-4b13-9c48-300a3e640a5f");
    private static readonly Guid TableId = Guid.Parse("b5f1a622-b2e4-47c4-b742-a9625a15d069");
    private static readonly Guid SessionId = Guid.Parse("5bf28221-ce44-4582-a906-bc69b12e6a77");
    private static readonly Guid OrderId = Guid.Parse("f6d11c6b-36ca-44e6-bf71-2b3b870b8fd4");
    private static readonly Guid ProductId = Guid.Parse("e17d81f4-1c6d-4eaf-bbcf-2d1d49d0e47c");

    [Fact]
    public async Task Preflight_accepts_a_usable_menu_and_available_tpvs()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new[] { MenuItem() }),
            Json(HttpStatusCode.OK, Array.Empty<TableInfo>()),
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Open")),
            Json(HttpStatusCode.OK, Array.Empty<OrderResponse>())
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        Exception? error = await Simulator.PreflightRestaurantAsync(
            client,
            Restaurant(),
            "pos-token",
            "kds-token"
        );

        Assert.Null(error);
        Assert.Equal(
            [
                (HttpMethod.Get, $"api/catalog/restaurants/{RestaurantId}/menu"),
                (HttpMethod.Get, $"api/dining/restaurants/{RestaurantId}/tables"),
                (HttpMethod.Get, $"api/cash-register/restaurants/{RestaurantId}/current"),
                (HttpMethod.Get, $"api/orders/kds?restaurantId={RestaurantId}&stationCode=CHEF"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
        Assert.Equal("Bearer pos-token", handler.Requests[0].Authorization);
        Assert.Equal("Bearer kds-token", handler.Requests[^1].Authorization);
    }

    [Fact]
    public async Task Preflight_reports_the_step_that_failed()
    {
        RecordingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("catalog unavailable"),
            }
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        Exception? error = await Simulator.PreflightRestaurantAsync(
            client,
            Restaurant(),
            "pos-token",
            "kds-token"
        );

        Assert.NotNull(error);
        Assert.Contains("falló la validación de menú TPV", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Cash_register_is_opened_when_the_current_shift_is_closed()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Closed")),
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Open"))
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        await Simulator.EnsureCashRegisterOpenAsync(
            client,
            Restaurant(),
            "pos-token",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                (HttpMethod.Get, $"api/cash-register/restaurants/{RestaurantId}/current"),
                (HttpMethod.Post, $"api/cash-register/restaurants/{RestaurantId}/open"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
    }

    [Fact]
    public async Task Cash_register_open_conflict_is_accepted_when_another_request_opened_it()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Closed")),
            new HttpResponseMessage(HttpStatusCode.Conflict),
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Open"))
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        await Simulator.EnsureCashRegisterOpenAsync(
            client,
            Restaurant(),
            "pos-token",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                (HttpMethod.Get, $"api/cash-register/restaurants/{RestaurantId}/current"),
                (HttpMethod.Post, $"api/cash-register/restaurants/{RestaurantId}/open"),
                (HttpMethod.Get, $"api/cash-register/restaurants/{RestaurantId}/current"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
    }

    [Fact]
    public async Task Cash_register_open_is_rejected_when_the_api_does_not_confirm_open_status()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Closed")),
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Opening"))
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Simulator.EnsureCashRegisterOpenAsync(
                client,
                Restaurant(),
                "pos-token",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("no confirmó que la caja quedara abierta", error.Message);
        Assert.Equal(
            [
                (HttpMethod.Get, $"api/cash-register/restaurants/{RestaurantId}/current"),
                (HttpMethod.Post, $"api/cash-register/restaurants/{RestaurantId}/open"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
    }

    [Fact]
    public async Task Cash_register_open_conflict_is_rethrown_when_status_remains_closed()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Closed")),
            new HttpResponseMessage(HttpStatusCode.Conflict),
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Closed"))
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Simulator.EnsureCashRegisterOpenAsync(
                client,
                Restaurant(),
                "pos-token",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Order_flow_submits_delivers_collects_payment_and_closes_the_session()
    {
        SessionInfo session = new(SessionId, RestaurantId, TableId, "Open");
        OrderResponse submittedOrder = Order("Submitted");
        OrderResponse deliveredOrder = Order("Delivered");
        BillResponse deliveredBill = Bill(
            outstanding: 10m,
            canRelease: false,
            [new BillOrderInfo(OrderId, "Delivered", "Unpaid", 10m, 0m, 0m, DateTime.UnixEpoch)]
        );
        BillResponse paidBill = Bill(outstanding: 0m, canRelease: true, []);
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, session),
            Json(HttpStatusCode.OK, submittedOrder),
            Json(HttpStatusCode.OK, deliveredOrder),
            Json(HttpStatusCode.OK, deliveredBill),
            Json(HttpStatusCode.OK, paidBill),
            Json(HttpStatusCode.OK, paidBill),
            Json(HttpStatusCode.OK, session)
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        bool completed = await Simulator.ProcessOrderAsync(
            client,
            Restaurant(),
            "pos-token",
            "kds-token",
            [MenuItem()],
            new HashSet<Guid> { ProductId },
            new TableInfo(TableId, "Mesa 04", "Available", true, null, false, "SALÓN"),
            TimeSpan.Zero,
            new Simulator.SimulatorRandom(17),
            new SemaphoreSlim(1, 1)
        );

        Assert.True(completed);
        Assert.Equal(
            [
                (HttpMethod.Post, $"api/dining/tables/{TableId}/sessions"),
                (HttpMethod.Post, "api/orders"),
                (HttpMethod.Post, $"api/orders/{OrderId}/submit"),
                (HttpMethod.Get, $"api/dining/sessions/{SessionId}/bill"),
                (HttpMethod.Post, $"api/dining/sessions/{SessionId}/checkout"),
                (HttpMethod.Get, $"api/dining/sessions/{SessionId}/bill"),
                (HttpMethod.Post, $"api/dining/sessions/{SessionId}/close"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
        Assert.Equal("Bearer pos-token", handler.Requests[0].Authorization);
        Assert.All(
            handler.Requests,
            request => Assert.Equal("Bearer pos-token", request.Authorization)
        );
    }

    [Fact]
    public async Task Cancellation_before_order_generation_finishes_without_querying_tables()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new[] { MenuItem() }),
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Open"))
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };
        using CancellationTokenSource stopCreating = new();
        stopCreating.Cancel();
        Simulator.RandomOrderScheduler scheduler = new(
            1,
            [RestaurantId],
            new Simulator.SimulatorRandom(17)
        );

        RestaurantRunResult result = await Simulator.RunRestaurantAsync(
            client,
            Restaurant(),
            "pos-token",
            "kds-token",
            2,
            scheduler,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            new Simulator.SimulatorRandom(17),
            new SemaphoreSlim(1, 1),
            stopCreating.Token
        );

        Assert.Equal(new RestaurantRunResult(0, 0), result);
        Assert.Equal(
            [
                $"api/catalog/restaurants/{RestaurantId}/menu",
                $"api/cash-register/restaurants/{RestaurantId}/current",
            ],
            handler.Requests.Select(request => request.Path)
        );
    }

    [Fact]
    public async Task Failed_submission_keeps_the_confirmed_order_and_does_not_cancel_its_session()
    {
        SessionInfo session = new(SessionId, RestaurantId, TableId, "Open");
        OrderResponse createdOrder = Order("Submitted");
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, session),
            Json(HttpStatusCode.OK, createdOrder),
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("submission rejected"),
            }
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        bool completed = await Simulator.ProcessOrderAsync(
            client,
            Restaurant(),
            "pos-token",
            "kds-token",
            [MenuItem()],
            new HashSet<Guid> { ProductId },
            new TableInfo(TableId, "Mesa 04", "Available", true, null, false, "SALÓN"),
            TimeSpan.Zero,
            new Simulator.SimulatorRandom(17),
            new SemaphoreSlim(1, 1)
        );

        Assert.False(completed);
        Assert.Equal(
            [
                (HttpMethod.Post, $"api/dining/tables/{TableId}/sessions"),
                (HttpMethod.Post, "api/orders"),
                (HttpMethod.Post, $"api/orders/{OrderId}/submit"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
    }

    [Fact]
    public async Task Failed_order_creation_cancels_the_empty_session()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new SessionInfo(SessionId, RestaurantId, TableId, "Open")),
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("order rejected"),
            },
            Json(HttpStatusCode.OK, new SessionInfo(SessionId, RestaurantId, TableId, "Cancelled"))
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        bool completed = await Simulator.ProcessOrderAsync(
            client,
            Restaurant(),
            "pos-token",
            "kds-token",
            [MenuItem()],
            new HashSet<Guid> { ProductId },
            new TableInfo(TableId, "Mesa 04", "Available", true, null, false, "SALÓN"),
            TimeSpan.Zero,
            new Simulator.SimulatorRandom(17),
            new SemaphoreSlim(1, 1)
        );

        Assert.False(completed);
        Assert.Equal(
            [
                (HttpMethod.Post, $"api/dining/tables/{TableId}/sessions"),
                (HttpMethod.Post, "api/orders"),
                (HttpMethod.Post, $"api/dining/sessions/{SessionId}/cancel"),
            ],
            handler.Requests.Select(request => (request.Method, request.Path))
        );
    }

    [Fact]
    public async Task Run_stops_generating_when_the_table_query_fails_and_reports_execution_error()
    {
        RecordingHandler handler = new(
            Json(HttpStatusCode.OK, new[] { MenuItem() }),
            Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Open")),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("tables unavailable"),
            }
        );
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };
        Simulator.RandomOrderScheduler scheduler = new(
            1,
            [RestaurantId],
            new Simulator.SimulatorRandom(17)
        );

        RestaurantRunResult result = await Simulator.RunRestaurantAsync(
            client,
            Restaurant(),
            "pos-token",
            "kds-token",
            1,
            scheduler,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            new Simulator.SimulatorRandom(17),
            new SemaphoreSlim(1, 1),
            CancellationToken.None
        );

        Assert.Equal(new RestaurantRunResult(0, 1), result);
        Assert.Equal(
            [
                $"api/catalog/restaurants/{RestaurantId}/menu",
                $"api/cash-register/restaurants/{RestaurantId}/current",
                $"api/dining/restaurants/{RestaurantId}/tables",
            ],
            handler.Requests.Select(request => request.Path)
        );
    }

    private static RestaurantOptions Restaurant() =>
        new()
        {
            Name = "Local de prueba",
            RestaurantId = RestaurantId,
            MaxProductsPerOrder = 1,
            Payment = new PaymentOptions
            {
                SplitProbability = 0,
                CashOverpayProbability = 0,
                TipPercentages = [0],
                Methods = [new PaymentMethodOptions { Name = "Card", Weight = 1 }],
            },
        };

    private static MenuItemResponse MenuItem() =>
        new(
            RestaurantId,
            ProductId,
            "SKU",
            "Producto",
            Guid.NewGuid(),
            "PLATOS",
            "Platos",
            10m,
            true,
            "CHEF",
            "Chef",
            1,
            DateTime.UnixEpoch
        );

    private static OrderResponse Order(string status) =>
        new(
            OrderId,
            RestaurantId,
            null,
            null,
            null,
            "Mesa 04",
            "",
            "DineIn",
            "Pos",
            "OnAccount",
            status,
            10m,
            1,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch,
            null,
            null,
            null,
            [],
            []
        );

    private static BillResponse Bill(
        decimal outstanding,
        bool canRelease,
        IReadOnlyList<BillOrderInfo> orders
    ) => new(Guid.NewGuid(), 10m, 0m, outstanding, canRelease, orders);

    private static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T content) =>
        new(statusCode) { Content = JsonContent.Create(content) };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<(HttpMethod Method, string Path, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(
                (
                    request.Method,
                    request.RequestUri!.PathAndQuery.TrimStart('/'),
                    request.Headers.Authorization?.ToString()
                )
            );
            return Task.FromResult(this.responses.Dequeue());
        }
    }
}
