using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public interface IOrderWriteStore
{
    Task<Order?> FindAsync(Guid id, CancellationToken ct);
    Task SaveWithEventAsync(Order order, object integrationEvent, CancellationToken ct);
}
