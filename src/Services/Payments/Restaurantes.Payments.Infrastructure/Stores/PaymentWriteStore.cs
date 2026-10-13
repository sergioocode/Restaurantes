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
    public Task SaveAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return dbContext.PayableOrders.SingleOrDefaultAsync(
            item => item.OrderId == orderId,
            cancellationToken
        );
    }

    public Task<PayableOrderCapture?> FindCaptureAsync(
        Guid orderId,
        Guid idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        return dbContext.Captures.SingleOrDefaultAsync(
            capture => capture.OrderId == orderId && capture.IdempotencyKey == idempotencyKey,
            cancellationToken
        );
    }

    public async Task SaveWithEventAsync(
        PayableOrder order,
        PayableOrderCapture capture,
        object integrationEvent,
        CancellationToken cancellationToken
    )
    {
        dbContext.Captures.Add(capture);
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

    public async Task SaveWithEventAsync(
        PayableOrder order,
        object integrationEvent,
        CancellationToken cancellationToken
    )
    {
        Type eventType = integrationEvent.GetType();
        DateTime occurredAtUtc = integrationEvent is PaymentRefunded refunded
            ? refunded.RefundedAtUtc
            : throw new NotSupportedException(eventType.FullName);
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
