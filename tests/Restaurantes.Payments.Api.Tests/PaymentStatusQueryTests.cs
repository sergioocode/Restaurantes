using Microsoft.EntityFrameworkCore;
using Restaurantes.Payments.Contracts.Responses;
using Restaurantes.Payments.Domain;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Payments.Infrastructure.Queries;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentStatusQueryTests
{
    [Fact]
    public async Task List_maps_open_partial_paid_and_refunded_orders_for_the_requested_restaurant()
    {
        await using PaymentWriteDbContext db = new(
            new DbContextOptionsBuilder<PaymentWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        Guid restaurantId = Guid.NewGuid();
        PayableOrder open = CreateOrder(restaurantId);
        PayableOrder partial = CreateOrder(restaurantId);
        partial.Capture(4m, 0m, "Card", "partial", Guid.NewGuid(), DateTime.UtcNow);
        PayableOrder paid = CreateOrder(restaurantId);
        paid.Capture(10m, 0m, "Cash", "paid", Guid.NewGuid(), DateTime.UtcNow);
        PayableOrder refunded = CreateOrder(restaurantId);
        refunded.Capture(10m, 0m, "Card", "refunded", Guid.NewGuid(), DateTime.UtcNow);
        refunded.Refund(DateTime.UtcNow);
        PayableOrder otherRestaurant = CreateOrder(Guid.NewGuid());
        db.PayableOrders.AddRange(open, partial, paid, refunded, otherRestaurant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PaymentStatusQuery query = new(db);
        var result = await query.ListAsync(
            restaurantId,
            [
                open.OrderId,
                partial.OrderId,
                paid.OrderId,
                refunded.OrderId,
                otherRestaurant.OrderId,
            ],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(4, result.Count);
        Dictionary<Guid, string> statuses = result.ToDictionary(
            item => item.OrderId,
            item => item.Status
        );
        Assert.Equal("Open", statuses[open.OrderId]);
        Assert.Equal("PartiallyPaid", statuses[partial.OrderId]);
        Assert.Equal("Paid", statuses[paid.OrderId]);
        Assert.Equal("Refunded", statuses[refunded.OrderId]);
    }

    private static PayableOrder CreateOrder(Guid restaurantId) =>
        PayableOrder.Create(
            Guid.NewGuid(),
            restaurantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DineIn",
            "Pos",
            10m,
            DateTime.UtcNow
        );
}
