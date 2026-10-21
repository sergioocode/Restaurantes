using System.Net;
using System.Net.Http.Json;
using Restaurantes.Orders.Contracts.Responses;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class SimulatorKitchenTests
{
    private static readonly Guid OrderId = Guid.Parse("f6d11c6b-36ca-44e6-bf71-2b3b870b8fd4");

    [Fact]
    public async Task Preparation_starts_pending_lines_and_waits_for_the_station_to_dispatch_them()
    {
        Guid lineId = Guid.NewGuid();
        OrderResponse preparing = Order("Submitted", [Line(lineId, "InPreparation")]);
        OrderResponse ready = Order("Ready", [Line(lineId, "Dispatched")]);
        RecordingHandler handler = new(preparing, ready);
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        OrderResponse result = await Simulator.SimulateKitchenAsync(
            client,
            "kds-token",
            Order("Submitted", [Line(lineId, "Pending")]),
            TimeSpan.Zero
        );

        Assert.Equal("Ready", result.Status);
        Assert.Equal(
            [
                "api/orders/" + OrderId + "/lines/" + lineId + "/start-preparation",
                "api/orders/" + OrderId + "/lines/" + lineId + "/ready",
            ],
            handler.Paths
        );
        Assert.All(handler.Authorization, value => Assert.Equal("Bearer kds-token", value));
    }

    [Fact]
    public async Task Primary_dispatch_waits_for_ready_and_dispatches_every_station_line()
    {
        Guid firstLineId = Guid.NewGuid();
        Guid secondLineId = Guid.NewGuid();
        OrderKitchenStationResponse readyStation = Station("Ready");
        OrderKitchenStationResponse dispatchedStation = Station("Dispatched");
        OrderResponse firstDispatch = Order(
            "Ready",
            [Line(firstLineId, "Dispatched", "CHEF"), Line(secondLineId, "Ready", "CHEF")],
            [readyStation]
        );
        OrderResponse completed = Order(
            "Delivered",
            [Line(firstLineId, "Dispatched", "CHEF"), Line(secondLineId, "Dispatched", "CHEF")],
            [dispatchedStation]
        );
        RecordingHandler handler = new(firstDispatch, completed);
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        OrderResponse result = await Simulator.SimulateKitchenAsync(
            client,
            "kds-token",
            Order(
                "Ready",
                [Line(firstLineId, "Ready", "CHEF"), Line(secondLineId, "Ready", "CHEF")],
                [readyStation]
            ),
            TimeSpan.Zero
        );

        Assert.Equal("Delivered", result.Status);
        Assert.Collection(
            handler.Paths,
            path => Assert.Equal($"api/orders/{OrderId}/lines/{firstLineId}/dispatch", path),
            path => Assert.Equal($"api/orders/{OrderId}/lines/{secondLineId}/dispatch", path)
        );
    }

    [Fact]
    public async Task Primary_dispatch_rejects_a_station_that_is_not_ready()
    {
        OrderResponse order = Order("Ready", [], [Station("InPreparation")]);
        RecordingHandler handler = new();
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Simulator.SimulateKitchenAsync(client, "kds-token", order, TimeSpan.Zero)
        );

        Assert.Contains("no puede despachar el pase Chef", error.Message);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Kitchen_completion_rejects_an_order_that_is_still_submitted()
    {
        OrderResponse order = Order("Submitted", []);
        using HttpClient client = new(new RecordingHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        };

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Simulator.SimulateKitchenAsync(client, "kds-token", order, TimeSpan.Zero)
        );

        Assert.Contains("no Ready ni Delivered", error.Message);
    }

    private static OrderResponse Order(
        string status,
        IReadOnlyList<OrderLineResponse> lines,
        IReadOnlyList<OrderKitchenStationResponse>? stations = null
    ) =>
        new(
            OrderId,
            Guid.NewGuid(),
            null,
            null,
            null,
            "Mesa 04",
            "",
            "DineIn",
            "Pos",
            "OnAccount",
            status,
            12m,
            1,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch,
            null,
            null,
            null,
            lines,
            stations ?? []
        );

    private static OrderLineResponse Line(
        Guid id,
        string kitchenStatus,
        string stationCode = "PASTAS"
    ) =>
        new(
            id,
            Guid.NewGuid(),
            "Pasta",
            12m,
            1,
            12m,
            "",
            null,
            "Pastas",
            stationCode,
            "Pastas",
            KitchenStatus: kitchenStatus
        );

    private static OrderKitchenStationResponse Station(string status) =>
        new("CHEF", "Chef", status, true, 1, null, null, null);

    private sealed class RecordingHandler(params OrderResponse[] replies) : HttpMessageHandler
    {
        private readonly Queue<OrderResponse> responses = new(replies);

        public List<string> Paths { get; } = [];

        public List<string?> Authorization { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri!.PathAndQuery.TrimStart('/'));
            Authorization.Add(request.Headers.Authorization?.ToString());
            OrderResponse response = responses.Dequeue();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(response),
                }
            );
        }
    }
}
