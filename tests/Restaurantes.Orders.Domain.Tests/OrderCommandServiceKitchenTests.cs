using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Domain;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderCommandServiceKitchenTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Kitchen_operations_return_null_when_order_is_missing()
    {
        RecordingStores stores = new();
        OrderCommandService service = CreateService(stores);
        Guid orderId = Guid.NewGuid();

        Assert.Null(
            await service.StartLinePreparationAsync(orderId, Guid.NewGuid(), CancellationToken.None)
        );
        Assert.Null(
            await service.MarkLineReadyAsync(orderId, Guid.NewGuid(), CancellationToken.None)
        );
        Assert.Null(
            await service.DispatchLineAsync(orderId, Guid.NewGuid(), CancellationToken.None)
        );
        Assert.Equal(0, stores.SaveCount);
    }

    [Fact]
    public async Task StartLinePreparation_saves_station_event_and_maps_order_state()
    {
        Order order = CreateSubmittedOrder([Line("CALIENTE", 1)]);
        OrderLine line = Assert.Single(order.Lines);
        RecordingStores stores = new(order);
        OrderCommandService service = CreateService(stores);

        var result = await service.StartLinePreparationAsync(
            order.Id,
            line.Id,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("InPreparation", result.Status);
        KitchenTicketPreparationStarted started = Assert.IsType<KitchenTicketPreparationStarted>(
            stores.IntegrationEvent
        );
        Assert.Equal("CALIENTE", started.ChangedStationCode);
        Assert.Equal(1, stores.SaveCount);
    }

    [Fact]
    public async Task MarkLineReady_emits_order_ready_when_it_completes_the_last_station()
    {
        Order order = CreateSubmittedOrder([Line("CALIENTE", 1, requiresPrimaryDispatch: false)]);
        OrderLine line = Assert.Single(order.Lines);
        order.StartLinePreparation(line.Id, CreatedAtUtc.AddMinutes(1));
        RecordingStores stores = new(order);
        OrderCommandService service = CreateService(stores);

        var result = await service.MarkLineReadyAsync(order.Id, line.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Ready", result.Status);
        Assert.IsType<OrderReady>(stores.IntegrationEvent);
        Assert.Equal(1, stores.SaveCount);
    }

    [Fact]
    public async Task MarkLineReady_emits_station_event_while_another_station_is_pending()
    {
        Order order = CreateSubmittedOrder([Line("CALIENTE", 1), Line("BEBIDAS", 2)]);
        OrderLine[] lines = order.Lines.ToArray();
        order.StartLinePreparation(lines[0].Id, CreatedAtUtc.AddMinutes(1));
        order.StartLinePreparation(lines[1].Id, CreatedAtUtc.AddMinutes(1));
        RecordingStores stores = new(order);
        OrderCommandService service = CreateService(stores);

        var result = await service.MarkLineReadyAsync(
            order.Id,
            lines[0].Id,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("InPreparation", result.Status);
        Assert.IsType<KitchenTicketReady>(stores.IntegrationEvent);
    }

    [Fact]
    public async Task DispatchLine_emits_station_event_then_order_ready_after_final_station()
    {
        Order order = CreateSubmittedOrder([
            Line("CALIENTE", 1, requiresPrimaryDispatch: true),
            Line("BEBIDAS", 2, requiresPrimaryDispatch: true),
        ]);
        OrderLine[] lines = order.Lines.ToArray();
        foreach (OrderLine line in lines)
        {
            order.StartLinePreparation(line.Id, CreatedAtUtc.AddMinutes(1));
            order.MarkLineReady(line.Id, CreatedAtUtc.AddMinutes(2));
        }
        RecordingStores stores = new(order);
        OrderCommandService service = CreateService(stores);

        var partial = await service.DispatchLineAsync(
            order.Id,
            lines[0].Id,
            CancellationToken.None
        );
        Assert.NotNull(partial);
        Assert.Equal("InPreparation", partial.Status);
        Assert.IsType<KitchenTicketDispatched>(stores.IntegrationEvent);

        var completed = await service.DispatchLineAsync(
            order.Id,
            lines[1].Id,
            CancellationToken.None
        );
        Assert.NotNull(completed);
        Assert.Equal("Ready", completed.Status);
        Assert.IsType<OrderReady>(stores.IntegrationEvent);
        Assert.Equal(2, stores.SaveCount);
    }

    private static OrderCommandService CreateService(RecordingStores stores) =>
        new(stores, stores, stores, stores, new FixedTimeProvider());

    private static Order CreateSubmittedOrder(IReadOnlyList<OrderLineDraft> lines)
    {
        Order order = Order.Create(
            Guid.NewGuid(),
            null,
            null,
            Guid.NewGuid(),
            "Pedido",
            "Ana",
            ServiceMode.Takeaway,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            lines,
            CreatedAtUtc
        );
        order.Submit(CreatedAtUtc.AddMinutes(1));
        return order;
    }

    private static OrderLineDraft Line(
        string stationCode,
        int priority,
        bool requiresPrimaryDispatch = false
    ) =>
        new(
            Guid.NewGuid(),
            $"Producto {stationCode}",
            10m,
            1,
            "",
            Guid.NewGuid(),
            "Categoría",
            stationCode,
            stationCode,
            requiresPrimaryDispatch,
            priority
        );

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(CreatedAtUtc.AddMinutes(5), TimeSpan.Zero);
    }

    private sealed class RecordingStores(Order? order = null)
        : IOrderWriteStore,
            IOrderCatalogStore,
            IOrderPaymentStore,
            IDiningSessionStore
    {
        public object? IntegrationEvent { get; private set; }
        public int SaveCount { get; private set; }

        public Task<Order?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(order?.Id == id ? order : null);

        public Task SaveWithEventAsync(
            Order savedOrder,
            object integrationEvent,
            CancellationToken ct
        )
        {
            IntegrationEvent = integrationEvent;
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, OrderCatalogProduct>> FindAsync(
            Guid restaurantId,
            IReadOnlyCollection<Guid> productIds,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyDictionary<Guid, OrderCatalogProduct>>(
                new Dictionary<Guid, OrderCatalogProduct>()
            );

        public Task<bool> IsPaidAsync(Guid orderId, CancellationToken ct) => Task.FromResult(false);

        public Task<bool> IsRefundedAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<DiningSessionValidation> EnsureOpenAsync(
            Guid diningSessionId,
            Guid restaurantId,
            Guid? tableId,
            string source,
            string serviceMode,
            string customerAccessToken,
            CancellationToken ct
        ) => Task.FromResult(new DiningSessionValidation(null, tableId));
    }
}
