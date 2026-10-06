using Restaurantes.Orders.Contracts.Responses;

namespace Restaurantes.Orders.Application;

public interface IManagementOrderQuery
{
    Task<IReadOnlyList<ManagementOrderResponse>> ListAsync(
        Guid restaurantId,
        DateOnly date,
        int offset,
        CancellationToken ct
    );
}
