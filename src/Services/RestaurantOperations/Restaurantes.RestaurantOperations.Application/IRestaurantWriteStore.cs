using Restaurantes.RestaurantOperations.Contracts;
using Restaurantes.RestaurantOperations.Domain;

namespace Restaurantes.RestaurantOperations.Application;

public interface IRestaurantWriteStore
{
    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);
    Task<Restaurant?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task SaveWithEventAsync(
        Restaurant restaurant,
        RestaurantChanged integrationEvent,
        CancellationToken cancellationToken
    );
}
