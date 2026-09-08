using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;

namespace Restaurantes.RestaurantOperations.Infrastructure.Stores;

public sealed class RestaurantReadStore(RestaurantReadDbContext dbContext) : IRestaurantReadStore
{
    public async Task<RestaurantResponse?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext
            .Restaurants.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new RestaurantResponse(
                item.Id,
                item.Code,
                item.Name,
                item.Address,
                item.IsActive,
                item.Version,
                item.UpdatedAtUtc
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RestaurantResponse>> ListAsync(
        CancellationToken cancellationToken
    )
    {
        return await dbContext
            .Restaurants.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new RestaurantResponse(
                item.Id,
                item.Code,
                item.Name,
                item.Address,
                item.IsActive,
                item.Version,
                item.UpdatedAtUtc
            ))
            .ToListAsync(cancellationToken);
    }
}
