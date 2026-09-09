using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;

namespace Restaurantes.Sales.Application;

public interface ISaleWriteStore
{
    Task<bool> WasProcessedAsync(Guid messageId, CancellationToken cancellationToken);
    Task<Sale?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<Sale?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
    Task<DeliveredOrderSnapshot?> FindDeliveredOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken
    );
    void Add(Sale sale);
    Task UpsertDeliveredOrderAsync(
        DeliveredOrderSnapshot deliveredOrder,
        CancellationToken cancellationToken
    );
    void AddIntegrationEvent(SaleCompleted integrationEvent);
    void MarkProcessed(Guid messageId, DateTime processedAtUtc);
    Task SaveAsync(CancellationToken cancellationToken);
}
