using Restaurantes.Payments.Domain;

namespace Restaurantes.Payments.Application;

public interface IPaymentWriteStore
{
    Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken);
    Task<PayableOrderCapture?> FindCaptureAsync(
        Guid orderId,
        Guid idempotencyKey,
        CancellationToken cancellationToken
    );
    Task SaveWithEventAsync(
        PayableOrder order,
        PayableOrderCapture capture,
        object integrationEvent,
        CancellationToken cancellationToken
    );
    Task SaveWithEventAsync(
        PayableOrder order,
        object integrationEvent,
        CancellationToken cancellationToken
    );
}
