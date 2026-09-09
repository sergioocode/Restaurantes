using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Payments.Domain;
using Restaurantes.Payments.Infrastructure.Persistence;
using Restaurantes.Payments.Infrastructure.Persistence.Write;

namespace Restaurantes.Payments.Infrastructure.Stores;

public sealed class PaymentWriteStore(PaymentWriteDbContext dbContext) : IPaymentWriteStore
{
    public Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return dbContext.PayableOrders.SingleOrDefaultAsync(
            item => item.OrderId == orderId,
            cancellationToken
        );
    }

    public async Task SaveWithEventAsync(
        PayableOrder order,
        object integrationEvent,
        CancellationToken cancellationToken
    )
    {
        Type eventType = integrationEvent.GetType();
        DateTime occurredAtUtc = integrationEvent switch
        {
            PaymentCaptured captured => captured.CapturedAtUtc,
            PaymentRefunded refunded => refunded.RefundedAtUtc,
            _ => throw new NotSupportedException(eventType.FullName),
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
