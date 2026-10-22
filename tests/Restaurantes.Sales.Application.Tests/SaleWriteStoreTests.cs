using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Restaurantes.Sales.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleWriteStoreTests
{
    [Fact]
    public async Task Persists_sale_inbox_outbox_and_delivered_order_markers()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SaleWriteDbContext db = new(
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        SaleWriteStore store = new(db);
        Guid messageId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        DateTime deliveredAt = DateTime.UtcNow;

        Assert.False(await store.WasProcessedAsync(messageId, ct));
        store.Add(
            new Sale
            {
                Id = saleId,
                RestaurantId = restaurantId,
                Orders = [new SaleOrder { OrderId = orderId, Amount = 12m }],
            }
        );
        SaleCompleted integrationEvent = new(
            saleId,
            restaurantId,
            "Pos",
            "Cash",
            12m,
            deliveredAt,
            [orderId],
            []
        );
        store.AddIntegrationEvent(integrationEvent);
        store.MarkProcessed(messageId, deliveredAt);
        await store.UpsertDeliveredOrderAsync(
            new(orderId, restaurantId, deliveredAt, "[{\"name\":\"Plato\"}]"),
            ct
        );
        await store.SaveAsync(ct);

        Assert.True(await store.WasProcessedAsync(messageId, ct));
        Assert.Equal(saleId, (await store.FindAsync(saleId, ct))!.Id);
        Assert.Equal(saleId, (await store.FindByOrderIdAsync(orderId, ct))?.Id);
        DeliveredOrderSnapshot marker = (await store.FindDeliveredOrderAsync(orderId, ct))!;
        Assert.Equal(restaurantId, marker.RestaurantId);
        Assert.Contains("Plato", marker.LinesJson);
        SaleCompleted persistedEvent = System.Text.Json.JsonSerializer.Deserialize<SaleCompleted>(
            Assert.Single(db.OutboxMessages).Payload
        )!;
        Assert.Equal(integrationEvent.SaleId, persistedEvent.SaleId);
        Assert.Equal(integrationEvent.RestaurantId, persistedEvent.RestaurantId);
        Assert.Equal(integrationEvent.Total, persistedEvent.Total);
        Assert.Equal(integrationEvent.OrderIds, persistedEvent.OrderIds);

        DateTime updatedAt = deliveredAt.AddMinutes(1);
        await store.UpsertDeliveredOrderAsync(
            new(orderId, restaurantId, updatedAt, "[{\"name\":\"Postre\"}]"),
            ct
        );
        await store.SaveAsync(ct);

        marker = (await store.FindDeliveredOrderAsync(orderId, ct))!;
        Assert.Equal(updatedAt, marker.DeliveredAtUtc);
        Assert.Contains("Postre", marker.LinesJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finds_the_sale_with_all_orders_and_payments(bool byOrder)
    {
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        Guid saleId = Guid.NewGuid();
        Guid firstOrder = Guid.NewGuid();
        Guid secondOrder = Guid.NewGuid();
        await using (SaleWriteDbContext writer = new(options))
        {
            writer.Sales.Add(
                new Sale
                {
                    Id = saleId,
                    Orders =
                    [
                        new() { OrderId = firstOrder, Amount = 10 },
                        new() { OrderId = secondOrder, Amount = 20 },
                    ],
                    Payments =
                    [
                        new()
                        {
                            PaymentId = Guid.NewGuid(),
                            Method = "Cash",
                            Amount = 5,
                        },
                        new()
                        {
                            PaymentId = Guid.NewGuid(),
                            Method = "Card",
                            Amount = 25,
                        },
                    ],
                }
            );
            writer.Sales.Add(new Sale { Id = Guid.NewGuid() });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using SaleWriteDbContext reader = new(options);
        SaleWriteStore store = new(reader);

        Sale? result = byOrder
            ? await store.FindByOrderIdAsync(firstOrder, TestContext.Current.CancellationToken)
            : await store.FindAsync(saleId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(saleId, result.Id);
        Assert.Equal(2, result.Orders.Count);
        Assert.Contains(result.Orders, order => order.OrderId == secondOrder);
        Assert.Equal(30m, result.Orders.Sum(order => order.Amount));
        Assert.Equal(2, result.Payments.Count);
        Assert.Equal(30m, result.Payments.Sum(payment => payment.Amount));
        Assert.Null(await store.FindAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Null(
            await store.FindByOrderIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)
        );
    }
}
