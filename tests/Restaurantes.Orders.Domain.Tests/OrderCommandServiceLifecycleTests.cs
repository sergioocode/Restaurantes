using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Domain;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderCommandServiceLifecycleTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Submit_returns_null_without_payment_lookup_when_order_is_missing()
    {
        TestStores stores = new(null);
        OrderCommandService service = CreateService(stores);

        Assert.Null(
            await service.SubmitAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)
        );
        Assert.Equal(0, stores.PaymentChecks);
        Assert.Null(stores.LastEvent);
    }

    [Fact]
    public async Task Submit_requires_immediate_payment_and_publishes_submitted_event_after_payment()
    {
        Order order = CreateOrder(
            OrderSource.CustomerQr,
            ServiceMode.DineIn,
            PaymentTiming.Immediate
        );
        TestStores stores = new(order);
        OrderCommandService service = CreateService(stores);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(order.Id, TestContext.Current.CancellationToken)
        );
        Assert.Equal(1, stores.PaymentChecks);
        Assert.Null(stores.LastEvent);

        stores.Paid = true;
        var response = await service.SubmitAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal("Submitted", response.Status);
        Assert.Equal(2, stores.PaymentChecks);
        OrderSubmitted submitted = Assert.IsType<OrderSubmitted>(stores.LastEvent);
        Assert.Equal(order.Id, submitted.OrderId);
        Assert.Equal(Now, submitted.SubmittedAtUtc);
        Assert.Single(submitted.Lines);
        Assert.Single(submitted.Stations);
    }

    [Fact]
    public async Task Deliver_requires_paid_takeaway_and_persists_delivery_event_when_kitchen_is_ready()
    {
        Order order = CreateOrder(OrderSource.Pos, ServiceMode.Takeaway, PaymentTiming.OnAccount);
        TestStores stores = new(order) { Paid = true };
        OrderCommandService service = CreateService(stores);
        Guid lineId = Assert.Single(order.Lines).Id;

        var submitted = await service.SubmitAsync(order.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(submitted);
        order.StartLinePreparation(lineId, Now.AddMinutes(1));
        order.MarkLineReady(lineId, Now.AddMinutes(2));

        var delivered = await service.DeliverAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(delivered);
        Assert.Equal("Delivered", delivered.Status);
        Assert.Equal(1, stores.PaymentChecks);
        OrderDelivered delivery = Assert.IsType<OrderDelivered>(stores.LastEvent);
        Assert.Equal(order.Id, delivery.OrderId);
        Assert.Equal(Now, delivery.DeliveredAtUtc);
        Assert.Single(delivery.Lines);
        Assert.Single(delivery.Stations);
    }

    private static OrderCommandService CreateService(TestStores stores) =>
        new(stores, stores, stores, stores, new FixedTimeProvider());

    private static Order CreateOrder(OrderSource source, ServiceMode mode, PaymentTiming timing)
    {
        Guid restaurantId = Guid.NewGuid();
        Guid? tableId = mode == ServiceMode.DineIn ? Guid.NewGuid() : null;
        Guid? sessionId = mode == ServiceMode.DineIn ? Guid.NewGuid() : null;
        return Order.Create(
            restaurantId,
            tableId,
            2,
            sessionId,
            "Mesa 2",
            mode == ServiceMode.Takeaway ? "Ana" : "",
            mode,
            source,
            timing,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Pasta",
                    12m,
                    1,
                    "",
                    Guid.NewGuid(),
                    "Pastas",
                    "PASTAS",
                    "Pastas",
                    false,
                    1,
                    "Ración"
                ),
            ],
            Now.AddHours(-1)
        );
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed class TestStores(Order? order)
        : IOrderWriteStore,
            IOrderCatalogStore,
            IOrderPaymentStore,
            IDiningSessionStore
    {
        public Order? Order { get; private set; } = order;
        public bool Paid { get; set; }
        public int PaymentChecks { get; private set; }
        public object? LastEvent { get; private set; }

        public Task<Order?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Order?.Id == id ? Order : null);

        public Task SaveWithEventAsync(
            Order savedOrder,
            object integrationEvent,
            CancellationToken ct
        )
        {
            Order = savedOrder;
            LastEvent = integrationEvent;
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

        public Task<bool> IsPaidAsync(Guid orderId, CancellationToken ct)
        {
            PaymentChecks++;
            return Task.FromResult(Paid);
        }

        public Task<bool> IsRefundedAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<DiningSessionValidation> EnsureOpenAsync(
            Guid diningSessionId,
            Guid restaurantId,
            Guid tableId,
            string source,
            string serviceMode,
            string customerAccessToken,
            CancellationToken ct
        ) => Task.FromResult(new DiningSessionValidation(2, null));
    }
}
