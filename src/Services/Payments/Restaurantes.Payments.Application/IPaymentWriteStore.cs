using Restaurantes.Payments.Domain;

namespace Restaurantes.Payments.Application;

public interface IPaymentWriteStore
{
    Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken);
    Task SaveWithEventAsync(
        PayableOrder order,
        object integrationEvent,
        CancellationToken cancellationToken
    );
}
