using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Infrastructure.Stores;

public sealed class OrderWriteStore(OrderWriteDbContext dbContext) : IOrderWriteStore
{
    public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext
            .Orders.Include(item => item.Lines)
            .Include(item => item.Stations)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task SaveWithEventAsync(
        Order order,
        object integrationEvent,
        CancellationToken cancellationToken
    )
    {
        if (dbContext.Entry(order).State == EntityState.Detached)
        {
            dbContext.Orders.Add(order);
        }

        Type eventType = integrationEvent.GetType();
        DateTime occurredAtUtc = integrationEvent switch
        {
            OrderCreated created => created.OccurredAtUtc,
            OrderSubmitted submitted => submitted.SubmittedAtUtc,
            OrderPreparationStarted started => started.PreparationStartedAtUtc,
            OrderReady ready => ready.ReadyAtUtc,
            KitchenTicketPreparationStarted started => started.PreparationStartedAtUtc,
            KitchenTicketReady ready => ready.ReadyAtUtc
                ?? ready
                    .Stations.Single(item => item.Code == ready.ChangedStationCode)
                    .ReadyAtUtc!.Value,
            KitchenTicketDispatched dispatched => dispatched.DispatchedAtUtc,
            OrderDelivered delivered => delivered.DeliveredAtUtc,
            OrderLineCancelled cancelled => cancelled.CancelledAtUtc,
            OrderCancelled cancelled => cancelled.CancelledAtUtc,
            _ => throw new NotSupportedException($"Unsupported order event: {eventType.FullName}"),
        };

        dbContext.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = eventType.FullName!,
                Payload = JsonSerializer.Serialize(integrationEvent, eventType),
                OccurredAtUtc = occurredAtUtc,
            }
        );
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
