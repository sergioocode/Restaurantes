using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;
using Restaurantes.Sales.Infrastructure.Persistence;
using Restaurantes.Sales.Infrastructure.Persistence.Write;

namespace Restaurantes.Sales.Infrastructure.Stores;

public sealed class SaleWriteStore(SaleWriteDbContext dbContext) : ISaleWriteStore
{
    public Task<bool> WasProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return dbContext.InboxMessages.AnyAsync(item => item.Id == messageId, cancellationToken);
    }

    public Task<Sale?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext
            .Sales.Include(item => item.Orders)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task<Sale?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return dbContext
            .Sales.Include(item => item.Orders)
            .SingleOrDefaultAsync(
                item => item.Orders.Any(order => order.OrderId == orderId),
                cancellationToken
            );
    }

    public async Task<DeliveredOrderSnapshot?> FindDeliveredOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken
    )
    {
        DeliveredOrderMarker? marker = await dbContext.DeliveredOrders.FindAsync(
            [orderId],
            cancellationToken
        );
        return marker is null
            ? null
            : new(marker.OrderId, marker.RestaurantId, marker.DeliveredAtUtc, marker.LinesJson);
    }

    public void Add(Sale sale)
    {
        dbContext.Sales.Add(sale);
    }

    public async Task UpsertDeliveredOrderAsync(
        DeliveredOrderSnapshot deliveredOrder,
        CancellationToken cancellationToken
    )
    {
        DeliveredOrderMarker? marker = await dbContext.DeliveredOrders.FindAsync(
            [deliveredOrder.OrderId],
            cancellationToken
        );
        if (marker is null)
        {
            dbContext.DeliveredOrders.Add(
                new DeliveredOrderMarker
                {
                    OrderId = deliveredOrder.OrderId,
                    RestaurantId = deliveredOrder.RestaurantId,
                    DeliveredAtUtc = deliveredOrder.DeliveredAtUtc,
                    LinesJson = deliveredOrder.LinesJson,
                }
            );
            return;
        }

        marker.DeliveredAtUtc = deliveredOrder.DeliveredAtUtc;
        marker.LinesJson = deliveredOrder.LinesJson;
    }

    public void AddIntegrationEvent(SaleCompleted integrationEvent)
    {
        dbContext.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = typeof(SaleCompleted).FullName!,
                Payload = JsonSerializer.Serialize(integrationEvent),
                OccurredAtUtc = integrationEvent.CompletedAtUtc,
            }
        );
    }

    public void MarkProcessed(Guid messageId, DateTime processedAtUtc)
    {
        dbContext.InboxMessages.Add(
            new InboxMessage { Id = messageId, ProcessedAtUtc = processedAtUtc }
        );
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
