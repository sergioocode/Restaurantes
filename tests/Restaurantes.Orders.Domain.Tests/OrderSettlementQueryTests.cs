using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Queries;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderSettlementQueryTests
{
    [Fact]
    public async Task List_filters_by_restaurant_and_marks_cancelled_orders()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Order active = CreateOrder(restaurantId);
        Order cancelled = CreateOrder(restaurantId);
        cancelled.Cancel("Cliente lo solicita", DateTime.UtcNow);
        db.Orders.AddRange(active, cancelled, CreateOrder(Guid.NewGuid()));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        OrderSettlementQuery query = new(db);
        IReadOnlyList<CashCloseOrderResponse> result = await query.ListAsync(
            restaurantId,
            0,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.OrderId == active.Id && !item.IsCancelled);
        Assert.Contains(result, item => item.OrderId == cancelled.Id && item.IsCancelled);
    }

    [Fact]
    public async Task List_applies_offset_and_caps_results_at_five_hundred()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Order[] orders = Enumerable.Range(0, 501).Select(_ => CreateOrder(restaurantId)).ToArray();
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        OrderSettlementQuery query = new(db);

        IReadOnlyList<CashCloseOrderResponse> firstPage = await query.ListAsync(
            restaurantId,
            0,
            TestContext.Current.CancellationToken
        );
        IReadOnlyList<CashCloseOrderResponse> nextPage = await query.ListAsync(
            restaurantId,
            500,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(500, firstPage.Count);
        Assert.Single(nextPage);
        Assert.Equal(orders.OrderBy(item => item.Id).First().Id, firstPage[0].OrderId);
    }

    private static OrderWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static Order CreateOrder(Guid restaurantId) =>
        Order.Create(
            restaurantId,
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 1",
            "",
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Café",
                    2m,
                    1,
                    "",
                    null,
                    "Bebidas",
                    "",
                    "",
                    false,
                    1,
                    RequiresKds: false
                ),
            ],
            DateTime.UtcNow
        );
}
