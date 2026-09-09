using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Contracts.Requests;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.RestaurantOperations.Domain;

namespace Restaurantes.RestaurantOperations.Application;

public sealed class RestaurantCommandService(IRestaurantWriteStore store, TimeProvider timeProvider)
{
    public async Task<RestaurantResponse?> CreateAsync(
        CreateRestaurantRequest request,
        CancellationToken cancellationToken
    )
    {
        string normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (await store.CodeExistsAsync(normalizedCode, cancellationToken))
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Restaurant restaurant = Restaurant.Create(
            normalizedCode,
            request.Name,
            request.Address,
            now
        );
        await store.SaveWithEventAsync(
            restaurant,
            ToIntegrationEvent(restaurant),
            cancellationToken
        );

        return ToResponse(restaurant);
    }

    public async Task<RestaurantResponse?> UpdateAsync(
        Guid id,
        UpdateRestaurantRequest request,
        CancellationToken cancellationToken
    )
    {
        Restaurant? restaurant = await store.FindAsync(id, cancellationToken);
        if (restaurant is null)
        {
            return null;
        }

        restaurant.Update(
            request.Name,
            request.Address,
            request.IsActive,
            timeProvider.GetUtcNow().UtcDateTime
        );
        await store.SaveWithEventAsync(
            restaurant,
            ToIntegrationEvent(restaurant),
            cancellationToken
        );

        return ToResponse(restaurant);
    }

    private static RestaurantChanged ToIntegrationEvent(Restaurant restaurant)
    {
        return new(
            restaurant.Id,
            restaurant.Code,
            restaurant.Name,
            restaurant.Address,
            restaurant.IsActive,
            restaurant.Version,
            restaurant.UpdatedAtUtc
        );
    }

    private static RestaurantResponse ToResponse(Restaurant restaurant)
    {
        return new(
            restaurant.Id,
            restaurant.Code,
            restaurant.Name,
            restaurant.Address,
            restaurant.IsActive,
            restaurant.Version,
            restaurant.UpdatedAtUtc
        );
    }
}
