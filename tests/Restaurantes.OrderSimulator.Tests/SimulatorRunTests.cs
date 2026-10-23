using System.Net;
using System.Net.Http.Json;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.OrderSimulator;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class SimulatorRunTests
{
    private static readonly Guid RestaurantId = Guid.Parse("a5ee0468-415f-47ea-a347-2c28d39bbf7e");
    private static readonly Guid ProductId = Guid.Parse("5bfe375a-2713-4683-8d08-b8859d150f24");
    private static readonly Guid FirstTableId = Guid.Parse("bd0a83a5-0146-4fc3-9024-a287c7bed020");
    private static readonly Guid SecondTableId = Guid.Parse("40e17858-6509-4cc8-9e60-58c56dfeaa53");
    private static readonly Guid SessionId = Guid.Parse("211e41d9-783f-4e7a-b25c-3488896869cc");
    private static readonly Guid OrderId = Guid.Parse("7073c92c-72ec-458e-ae55-2422c3edf28f");

    [Fact]
    public async Task Run_schedules_available_tables_then_drains_the_started_order_on_cancellation()
    {
        using CancellationTokenSource stopCreating = new();
        RecordingHandler handler = new(stopCreating);
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };
        Simulator.RandomOrderScheduler scheduler = new(
            1,
            [RestaurantId],
            new Simulator.SimulatorRandom(17)
        );

        RestaurantRunResult result = await Simulator
            .RunRestaurantAsync(
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
            )
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(new RestaurantRunResult(0, 0), result);
        Assert.Equal(1, handler.TableQueries);
        Assert.Single(handler.Requests, path => path == "api/orders");
        Assert.Contains($"api/dining/sessions/{SessionId}/close", handler.Requests);
        Assert.Equal("Bearer pos-token", handler.Authorizations[0]);
        Assert.All(handler.Authorizations, value => Assert.Equal("Bearer pos-token", value));
    }

    private static RestaurantOptions Restaurant() =>
        new()
        {
            Name = "Simulación concurrente",
            RestaurantId = RestaurantId,
            MaxProductsPerOrder = 1,
            Payment = new PaymentOptions
            {
                SplitProbability = 0,
                CashOverpayProbability = 0,
                TipPercentages = [1],
                Methods =
                [
                    new PaymentMethodOptions { Name = "Card", Weight = 1 },
                    new PaymentMethodOptions { Name = "Cash", Weight = 1 },
                    new PaymentMethodOptions { Name = "Transfer", Weight = 1 },
                ],
            },
        };

    private static MenuItemResponse MenuItem() =>
        new(
            RestaurantId,
            ProductId,
            "SKU",
            "Plato",
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
            "Mesa 01",
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

    private static BillResponse Bill(bool delivered, bool paid) =>
        new(
            Guid.NewGuid(),
            10m,
            0m,
            paid ? 0m : 10m,
            paid,
            delivered
                ?
                [
                    new BillOrderInfo(
                        OrderId,
                        "Delivered",
                        paid ? "Paid" : "Unpaid",
                        10m,
                        paid ? 10m : 0m,
                        0m,
                        DateTime.UnixEpoch
                    ),
                ]
                : []
        );

    private sealed class RecordingHandler(CancellationTokenSource stopCreating) : HttpMessageHandler
    {
        private int billReads;

        public List<string> Requests { get; } = [];

        public List<string?> Authorizations { get; } = [];

        public int TableQueries { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.PathAndQuery.TrimStart('/');
            Requests.Add(path);
            Authorizations.Add(request.Headers.Authorization?.ToString());
            if (path == $"api/dining/restaurants/{RestaurantId}/tables")
            {
                TableQueries++;
                return Task.FromResult(
                    Json(
                        HttpStatusCode.OK,
                        new[] { Table(FirstTableId, "Mesa 01"), Table(SecondTableId, "Mesa 02") }
                    )
                );
            }

            if (
                path == $"api/dining/tables/{FirstTableId}/sessions"
                || path == $"api/dining/tables/{SecondTableId}/sessions"
            )
            {
                stopCreating.Cancel();
                Guid tableId = path.Contains(FirstTableId.ToString(), StringComparison.Ordinal)
                    ? FirstTableId
                    : SecondTableId;
                return Task.FromResult(
                    Json(
                        HttpStatusCode.OK,
                        new SessionInfo(SessionId, RestaurantId, tableId, "Open")
                    )
                );
            }

            if (path == $"api/catalog/restaurants/{RestaurantId}/menu")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, new[] { MenuItem() }));
            }

            if (path == $"api/cash-register/restaurants/{RestaurantId}/current")
            {
                return Task.FromResult(
                    Json(HttpStatusCode.OK, new CashRegisterStatusResponse("Open"))
                );
            }

            if (path == "api/orders")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, Order("Submitted")));
            }

            if (path == $"api/orders/{OrderId}/submit")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, Order("Ready")));
            }

            if (path == $"api/orders/{OrderId}/deliver")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, Order("Delivered")));
            }

            if (path == $"api/dining/sessions/{SessionId}/bill")
            {
                bool paid = Interlocked.Increment(ref billReads) > 1;
                return Task.FromResult(Json(HttpStatusCode.OK, Bill(delivered: true, paid)));
            }

            if (path == $"api/dining/sessions/{SessionId}/checkout")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, Bill(delivered: true, paid: true)));
            }

            if (path == $"api/dining/sessions/{SessionId}/close")
            {
                return Task.FromResult(
                    Json(
                        HttpStatusCode.OK,
                        new SessionInfo(SessionId, RestaurantId, FirstTableId, "Closed")
                    )
                );
            }

            throw new InvalidOperationException($"Unexpected simulator request: {path}");
        }

        private static TableInfo Table(Guid id, string label) =>
            new(id, label, "Available", true, null, false, "SALÓN");

        private static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T content) =>
            new(statusCode) { Content = JsonContent.Create(content) };
    }
}
