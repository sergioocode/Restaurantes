using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleCommandServiceTests
{
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
            Captured(orderId, restaurantId, accountId, 4m, 4m, false, "Cash"),
            TestContext.Current.CancellationToken
        );

        Assert.Empty(store.Completed);
        Assert.Equal("Pending", Assert.Single(store.Sales).Status);

        await service.ProcessAsync(
            Guid.NewGuid(),
            Captured(orderId, restaurantId, accountId, 6m, 10m, true, "Card"),
            TestContext.Current.CancellationToken
        );

        SaleCompleted completed = Assert.Single(store.Completed);
        Assert.Equal(accountId, completed.SaleId);
        Assert.Equal(10m, completed.Total);
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

    private static PaymentCaptured Captured(
        Guid orderId,
        Guid restaurantId,
        Guid accountId,
        decimal amount,
        decimal amountPaid,
        bool fullyPaid,
        string method,
        int orderCount = 1,
        Guid? transactionId = null
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
            SaleGroupId: accountId
        );

    private sealed class InMemorySaleStore : ISaleWriteStore
    {
        private readonly HashSet<Guid> processed = [];
        public List<Sale> Sales { get; } = [];
        public List<SaleCompleted> Completed { get; } = [];

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
        ) => Task.FromResult<DeliveredOrderSnapshot?>(null);

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
