using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Domain;

namespace Restaurantes.RestaurantOperations.Application;

public interface IRestaurantWriteStore
{
    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);
    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<Restaurant?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task SaveWithEventAsync(
        Restaurant restaurant,
        RestaurantChanged integrationEvent,
        CancellationToken cancellationToken
    );
}
