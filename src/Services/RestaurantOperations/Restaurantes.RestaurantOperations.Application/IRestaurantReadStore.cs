using Restaurantes.RestaurantOperations.Contracts.Responses;

namespace Restaurantes.RestaurantOperations.Application;

public interface IRestaurantReadStore
{
    Task<RestaurantResponse?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<RestaurantResponse>> ListAsync(CancellationToken cancellationToken);
}
