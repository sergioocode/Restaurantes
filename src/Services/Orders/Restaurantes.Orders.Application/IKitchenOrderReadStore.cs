using Restaurantes.Orders.Contracts;

namespace Restaurantes.Orders.Application;

public interface IKitchenOrderReadStore
{
    Task<OrderResponse?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OrderResponse>> ListAsync(
        Guid restaurantId,
        string? stationCode,
        CancellationToken ct
    );
}
