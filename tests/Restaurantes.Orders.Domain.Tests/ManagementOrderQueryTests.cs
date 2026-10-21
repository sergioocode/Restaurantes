using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Queries;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class ManagementOrderQueryTests
{
    [Fact]
    public async Task List_filters_by_restaurant_and_business_date_and_maps_payment_and_lines()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        DateOnly businessDate = new(2026, 10, 21);
        DateTime createdAt = new(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc);
        Order order = CreateOrder(restaurantId, createdAt, businessDate);
        OrderLine cancelledLine = order.Lines.Last();
        order.CancelLine(cancelledLine.Id, "Cliente lo ha cancelado", createdAt.AddMinutes(1));
        db.Orders.AddRange(
            order,
            CreateOrder(restaurantId, createdAt, businessDate.AddDays(-1)),
            CreateOrder(Guid.NewGuid(), createdAt, businessDate)
        );
        db.OrderPayments.Add(
            new OrderPaymentProjection
            {
                OrderId = order.Id,
                PaymentId = Guid.NewGuid(),
                Amount = 7.5m,
                Method = "Card",
                CapturedAtUtc = createdAt.AddMinutes(5),
                Status = "Paid",
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        ManagementOrderQuery query = new(db, TimeZoneInfo.FindSystemTimeZoneById("UTC"));

        IReadOnlyList<ManagementOrderResponse> result = await query.ListAsync(
            restaurantId,
            businessDate,
            0,
            TestContext.Current.CancellationToken
        );

        ManagementOrderResponse response = Assert.Single(result);
        Assert.Equal(order.Id, response.Id);
        Assert.Equal(restaurantId, response.RestaurantId);
        Assert.Equal("Mesa 4", response.TableLabel);
        Assert.Equal("Pos", response.Source);
        Assert.Equal("Draft", response.Status);
        Assert.Equal(8m, response.Total);
        Assert.Equal(createdAt, response.CreatedAtUtc);
        Assert.Equal(new DateTimeOffset(createdAt, TimeSpan.Zero), response.CreatedAtLocal);
        Assert.Equal("Card", response.PaymentMethod);
        Assert.Equal(
            new DateTimeOffset(createdAt.AddMinutes(5), TimeSpan.Zero),
            response.PaymentCapturedAtLocal
        );
        Assert.Equal(2, response.Lines.Count);
        ManagementOrderLineResponse cancelled = Assert.Single(
            response.Lines,
            line => line.Status == "Cancelled"
        );
        ManagementOrderLineResponse active = Assert.Single(
            response.Lines,
            line => line.Status == "Active"
        );
        Assert.Equal("Café", active.ProductName);
        Assert.Equal(2, active.Quantity);
        Assert.Equal("Taza", active.Format);
        Assert.Equal(0.5m, Assert.Single(active.Extras!).UnitPrice);
        Assert.Equal(2, response.GuestCount);
    }

    [Fact]
    public async Task List_uses_local_date_window_for_orders_without_business_date_and_applies_offset()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        DateOnly date = new(2026, 10, 21);
        TimeZoneInfo madrid = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        Order newest = CreateOrder(
            restaurantId,
            new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc),
            null
        );
        Order older = CreateOrder(
            restaurantId,
            new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc),
            null
        );
        Order previousLocalDay = CreateOrder(
            restaurantId,
            new DateTime(2026, 10, 20, 21, 59, 59, DateTimeKind.Utc),
            null
        );
        db.Orders.AddRange(newest, older, previousLocalDay);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        ManagementOrderQuery query = new(db, madrid);

        IReadOnlyList<ManagementOrderResponse> result = await query.ListAsync(
            restaurantId,
            date,
            1,
            TestContext.Current.CancellationToken
        );

        ManagementOrderResponse response = Assert.Single(result);
        Assert.Equal(older.Id, response.Id);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 21, 11, 0, 0, TimeSpan.FromHours(2)),
            response.CreatedAtLocal
        );
        Assert.Null(response.PaymentMethod);
        Assert.Null(response.PaymentCapturedAtLocal);
    }

    private static OrderWriteDbContext CreateDatabase()
    {
        DbContextOptions<OrderWriteDbContext> options =
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new OrderWriteDbContext(options);
    }

    private static Order CreateOrder(
        Guid restaurantId,
        DateTime createdAt,
        DateOnly? businessDate
    ) =>
        Order.Create(
            restaurantId,
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 4",
            "",
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Café",
                    4m,
                    2,
                    "",
                    null,
                    "Bebidas",
                    "",
                    "",
                    false,
                    1,
                    "Taza",
                    3.5m,
                    [new OrderLineExtra(Guid.NewGuid(), "Leche", 0.5m, 1)],
                    false
                ),
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Té",
                    3.5m,
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
            createdAt,
            businessDate: businessDate
        );
}
