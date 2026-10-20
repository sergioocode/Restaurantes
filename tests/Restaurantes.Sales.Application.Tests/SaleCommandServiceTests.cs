using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleCommandServiceTests
{
    [Fact]
    public async Task Sale_id_uses_transaction_then_payment_when_group_id_is_missing()
    {
        InMemorySaleStore store = new();
        SaleCommandService service = new(store, TimeProvider.System);
        Guid transactionId = Guid.NewGuid();
        PaymentCaptured transactionCapture = Captured(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            5m,
            5m,
            true,
            "Cash"
        ) with
        {
            SaleGroupId = null,
            PaymentTransactionId = transactionId,
        };
        PaymentCaptured paymentCapture = Captured(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            5m,
            5m,
            true,
            "Cash"
        ) with
        {
            SaleGroupId = Guid.Empty,
            PaymentTransactionId = Guid.Empty,
        };

        await service.ProcessAsync(
            Guid.NewGuid(),
            transactionCapture,
            TestContext.Current.CancellationToken
        );
        await service.ProcessAsync(
            Guid.NewGuid(),
            paymentCapture,
            TestContext.Current.CancellationToken
        );

        Assert.Contains(store.Sales, sale => sale.Id == transactionId);
        Assert.Contains(store.Sales, sale => sale.Id == paymentCapture.PaymentId);
    }

    [Fact]
    public async Task Delivered_snapshot_is_copied_to_new_sale_order()
    {
        InMemorySaleStore store = new();
        DateTime deliveredAt = DateTime.UtcNow.AddMinutes(-1);
        store.DeliveredOrder = new DeliveredOrderSnapshot(
            Guid.Empty,
            Guid.NewGuid(),
            deliveredAt,
            "[{\"name\":\"Pasta\"}]"
        );
        SaleCommandService service = new(store, TimeProvider.System);
        PaymentCaptured capture = Captured(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            5m,
            5m,
            true,
            "Cash"
        );
        store.DeliveredOrder = store.DeliveredOrder with { OrderId = capture.OrderId };

        await service.ProcessAsync(Guid.NewGuid(), capture, TestContext.Current.CancellationToken);

        SaleOrder order = Assert.Single(Assert.Single(store.Sales).Orders);
        Assert.True(order.IsDelivered);
        Assert.Equal(deliveredAt, order.DeliveredAtUtc);
        Assert.Equal(store.DeliveredOrder.LinesJson, order.LinesJson);
    }

    [Fact]
    public async Task Split_payments_complete_one_sale_only_after_the_account_is_paid()
    {
        InMemorySaleStore store = new();
        SaleCommandService service = new(store, TimeProvider.System);
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();

        await service.ProcessAsync(
            Guid.NewGuid(),
            Captured(orderId, restaurantId, accountId, 4m, 4m, false, "Cash", tipAmount: 0.5m),
            TestContext.Current.CancellationToken
        );

        Assert.Empty(store.Completed);
        Assert.Equal("Pending", Assert.Single(store.Sales).Status);

        await service.ProcessAsync(
            Guid.NewGuid(),
            Captured(orderId, restaurantId, accountId, 6m, 10m, true, "Card", tipAmount: 1m),
            TestContext.Current.CancellationToken
        );

        SaleCompleted completed = Assert.Single(store.Completed);
        Assert.Equal(accountId, completed.SaleId);
        Assert.Equal(10m, completed.Total);
        Assert.Equal(1.5m, completed.TipTotal);
        Assert.Equal("Mixed", completed.PaymentMethod);
        Assert.Equal([orderId], completed.OrderIds);
        Assert.Contains(
            completed.PaymentMethods!,
            x => x.Method == "Cash" && x.Payments == 1 && x.Amount == 4m
        );
        Assert.Contains(
            completed.PaymentMethods!,
            x => x.Method == "Card" && x.Payments == 1 && x.Amount == 6m
        );
        Assert.Single(store.Sales);
    }

    [Fact]
    public async Task Multiple_orders_complete_only_after_every_order_is_fully_paid()
    {
        InMemorySaleStore store = new();
        SaleCommandService service = new(store, TimeProvider.System);
        Guid restaurantId = Guid.NewGuid();
        Guid firstOrder = Guid.NewGuid();
        Guid secondOrder = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        Guid firstTransaction = Guid.NewGuid();

        await service.ProcessAsync(
            Guid.NewGuid(),
            Captured(
                firstOrder,
                restaurantId,
                accountId,
                3m,
                3m,
                true,
                "Cash",
                2,
                firstTransaction
            ),
            TestContext.Current.CancellationToken
        );
        Assert.Empty(store.Completed);

        await service.ProcessAsync(
            Guid.NewGuid(),
            Captured(
                secondOrder,
                restaurantId,
                accountId,
                2m,
                2m,
                false,
                "Cash",
                2,
                firstTransaction
            ),
            TestContext.Current.CancellationToken
        );
        Assert.Empty(store.Completed);

        await service.ProcessAsync(
            Guid.NewGuid(),
            Captured(secondOrder, restaurantId, accountId, 5m, 7m, true, "Cash", 2),
            TestContext.Current.CancellationToken
        );

        SaleCompleted completed = Assert.Single(store.Completed);
        Assert.Equal(10m, completed.Total);
        Assert.Equal(2, completed.OrderIds.Count);
        Assert.Equal(2, Assert.Single(completed.PaymentMethods!).Payments);
        Assert.Equal(10m, Assert.Single(completed.PaymentMethods!).Amount);
        Assert.Single(store.Sales);
    }

    [Fact]
    public async Task Refund_marks_the_matching_sale_order_unpaid_and_is_idempotent()
    {
        Guid orderId = Guid.NewGuid();
        SaleOrder order = new()
        {
            OrderId = orderId,
            IsPaid = true,
            Amount = 12m,
        };
        Sale sale = new() { Orders = [order] };
        InMemorySaleStore store = new();
        store.Sales.Add(sale);
        SaleCommandService service = new(store, TimeProvider.System);
        Guid messageId = Guid.NewGuid();
        PaymentRefunded refund = new(
            Guid.NewGuid(),
            orderId,
            Guid.NewGuid(),
            12m,
            "Card",
            "DineIn",
            "Pos",
            "Customer request",
            DateTime.UtcNow
        );

        await service.ProcessAsync(messageId, refund, TestContext.Current.CancellationToken);
        await service.ProcessAsync(messageId, refund, TestContext.Current.CancellationToken);

        Assert.False(order.IsPaid);
        Assert.Equal("Pending", sale.Status);
    }

    [Fact]
    public async Task Delivered_event_updates_existing_order_and_filters_cancelled_lines()
    {
        Guid orderId = Guid.NewGuid();
        DateTime deliveredAt = DateTime.UtcNow;
        SaleOrder order = new() { OrderId = orderId };
        InMemorySaleStore store = new();
        store.Sales.Add(new Sale { Orders = [order] });
        SaleCommandService service = new(store, TimeProvider.System);
        Restaurantes.Orders.Contracts.Events.OrderDelivered delivered = new(
            orderId,
            Guid.NewGuid(),
            null,
            null,
            null,
            "",
            "",
            "DineIn",
            "Pos",
            "AtCheckout",
            1,
            deliveredAt,
            deliveredAt,
            deliveredAt,
            deliveredAt,
            deliveredAt,
            [
                new Restaurantes.Orders.Contracts.Events.OrderLineSnapshot(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Soup",
                    5m,
                    2,
                    "",
                    null,
                    "",
                    "",
                    "",
                    Extras:
                    [
                        new Restaurantes.Orders.Contracts.Events.OrderLineExtraSnapshot(
                            Guid.NewGuid(),
                            "Bread",
                            1m,
                            1,
                            0.1m
                        ),
                    ]
                ),
                new Restaurantes.Orders.Contracts.Events.OrderLineSnapshot(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Cancelled soup",
                    9m,
                    1,
                    "",
                    null,
                    "",
                    "",
                    "",
                    Status: "Cancelled"
                ),
            ],
            []
        );

        await service.ProcessAsync(
            Guid.NewGuid(),
            delivered,
            TestContext.Current.CancellationToken
        );

        Assert.True(order.IsDelivered);
        Assert.Equal(deliveredAt, order.DeliveredAtUtc);
        SaleLineSnapshot snapshot = Assert.Single(
            System.Text.Json.JsonSerializer.Deserialize<List<SaleLineSnapshot>>(order.LinesJson)!
        );
        Assert.Equal("Soup", snapshot.ProductName);
        Assert.Equal("Bread", Assert.Single(snapshot.Extras!).ProductName);
    }

    [Fact]
    public async Task Payments_for_one_sale_reject_different_business_dates()
    {
        InMemorySaleStore store = new();
        SaleCommandService service = new(store, TimeProvider.System);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        PaymentCaptured firstPayment = Captured(
            orderId,
            restaurantId,
            accountId,
            3m,
            3m,
            false,
            "Cash"
        ) with
        {
            BusinessDate = new DateOnly(2026, 10, 20),
        };
        PaymentCaptured secondPayment = Captured(
            orderId,
            restaurantId,
            accountId,
            7m,
            10m,
            true,
            "Cash"
        ) with
        {
            BusinessDate = new DateOnly(2026, 10, 21),
        };

        await service.ProcessAsync(
            Guid.NewGuid(),
            firstPayment,
            TestContext.Current.CancellationToken
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessAsync(
                Guid.NewGuid(),
                secondPayment,
                TestContext.Current.CancellationToken
            )
        );
    }

    private static PaymentCaptured Captured(
        Guid orderId,
        Guid restaurantId,
        Guid accountId,
        decimal amount,
        decimal amountPaid,
        bool fullyPaid,
        string method,
        int orderCount = 1,
        Guid? transactionId = null,
        decimal tipAmount = 0
    ) =>
        new(
            Guid.NewGuid(),
            orderId,
            restaurantId,
            amount,
            method,
            "DineIn",
            "Pos",
            "",
            DateTime.UtcNow,
            transactionId ?? Guid.NewGuid(),
            orderCount,
            AmountPaid: amountPaid,
            IsFullyPaid: fullyPaid,
            SaleGroupId: accountId,
            TipAmount: tipAmount
        );

    private sealed class InMemorySaleStore : ISaleWriteStore
    {
        private readonly HashSet<Guid> processed = [];
        public List<Sale> Sales { get; } = [];
        public List<SaleCompleted> Completed { get; } = [];
        public DeliveredOrderSnapshot? DeliveredOrder { get; set; }

        public Task<bool> WasProcessedAsync(Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(processed.Contains(messageId));

        public Task<Sale?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Sales.SingleOrDefault(sale => sale.Id == id));

        public Task<Sale?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(
                Sales.SingleOrDefault(sale => sale.Orders.Any(order => order.OrderId == orderId))
            );

        public Task<DeliveredOrderSnapshot?> FindDeliveredOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken
        ) => Task.FromResult(DeliveredOrder?.OrderId == orderId ? DeliveredOrder : null);

        public void Add(Sale sale) => Sales.Add(sale);

        public Task UpsertDeliveredOrderAsync(
            DeliveredOrderSnapshot deliveredOrder,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public void AddIntegrationEvent(SaleCompleted integrationEvent) =>
            Completed.Add(integrationEvent);

        public void MarkProcessed(Guid messageId, DateTime processedAtUtc) =>
            processed.Add(messageId);

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
