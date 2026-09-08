using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts;
using Restaurantes.RestaurantOperations.Domain;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;

namespace Restaurantes.RestaurantOperations.Infrastructure.Stores;

public sealed class RestaurantWriteStore(RestaurantWriteDbContext dbContext) : IRestaurantWriteStore
{
    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken)
    {
        return dbContext.Restaurants.AnyAsync(
            item => item.Code == normalizedCode,
            cancellationToken
        );
    }

    public Task<Restaurant?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Restaurants.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task SaveWithEventAsync(
        Restaurant restaurant,
        RestaurantChanged integrationEvent,
        CancellationToken cancellationToken
    )
    {
        if (dbContext.Entry(restaurant).State == EntityState.Detached)
        {
            dbContext.Restaurants.Add(restaurant);
        }

        dbContext.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = typeof(RestaurantChanged).FullName!,
                Payload = JsonSerializer.Serialize(integrationEvent),
                OccurredAtUtc = integrationEvent.OccurredAtUtc,
            }
        );

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
