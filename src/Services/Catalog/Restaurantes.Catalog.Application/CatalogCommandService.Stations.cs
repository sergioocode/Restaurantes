using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Application;

public sealed partial class CatalogCommandService
{
    private static readonly (
        string Code,
        string Name,
        bool IsPrimary,
        bool RequiresPrimaryDispatch,
        int Priority
    )[] DefaultStations =
    [
        ("CHEF", "Vista completa", true, false, 0),
        ("ENTRANTES", "Entrantes", false, true, 1),
        ("BEBIDAS", "Bebidas", false, false, 1),
        ("PASTAS", "Pastas", false, true, 2),
        ("CARNES", "Carnes", false, true, 2),
        ("POSTRES", "Postres", false, false, 3),
    ];

    public async Task<KitchenStationResponse?> CreateStationAsync(
        Guid restaurantId,
        CreateKitchenStationRequest request,
        CancellationToken ct
    )
    {
        string code = request.Code.Trim().ToUpperInvariant();
        if (await store.FindStationByCodeAsync(restaurantId, code, ct) is not null)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RestaurantKitchenStation item = RestaurantKitchenStation.Create(
            restaurantId,
            code,
            request.Name,
            request.IsPrimary,
            request.RequiresPrimaryDispatch,
            request.Priority,
            now
        );
        if (item.IsPrimary && await store.HasOtherActivePrimaryStationAsync(restaurantId, null, ct))
        {
            throw new InvalidOperationException(
                "Only one active primary KDS is allowed per restaurant."
            );
        }
        await SaveStationAsync(item, now, ct);
        return ToResponse(item);
    }

    public async Task<KitchenStationResponse?> UpdateStationAsync(
        Guid restaurantId,
        Guid stationId,
        UpdateKitchenStationRequest request,
        CancellationToken ct
    )
    {
        RestaurantKitchenStation? item = await store.FindStationAsync(restaurantId, stationId, ct);
        if (item is null)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (
            request.IsActive
            && request.IsPrimary
            && await store.HasOtherActivePrimaryStationAsync(restaurantId, stationId, ct)
        )
        {
            throw new InvalidOperationException(
                "Only one active primary KDS is allowed per restaurant."
            );
        }
        item.Update(
            request.Name,
            request.IsActive,
            request.IsPrimary,
            request.RequiresPrimaryDispatch,
            request.Priority,
            now
        );
        await SaveStationAsync(item, now, ct);
        return ToResponse(item);
    }

    public async Task<IReadOnlyList<KitchenStationResponse>> EnsureDefaultStationsAsync(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        List<KitchenStationResponse> result = [];
        foreach (
            (
                string code,
                string name,
                bool isPrimary,
                bool requiresPrimaryDispatch,
                int priority
            ) in DefaultStations
        )
        {
            RestaurantKitchenStation? item = await store.FindStationByCodeAsync(
                restaurantId,
                code,
                ct
            );
            if (item is null)
            {
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                item = RestaurantKitchenStation.Create(
                    restaurantId,
                    code,
                    name,
                    isPrimary,
                    requiresPrimaryDispatch,
                    priority,
                    now
                );
                await SaveStationAsync(item, now, ct);
            }
            result.Add(ToResponse(item));
        }
        return result;
    }

    private async Task SaveStationAsync(
        RestaurantKitchenStation item,
        DateTime now,
        CancellationToken ct
    )
    {
        KitchenStationChanged message = new(
            item.Id,
            item.RestaurantId,
            item.Code,
            item.Name,
            item.IsPrimary,
            item.RequiresPrimaryDispatch,
            item.Priority,
            item.IsActive,
            item.Version,
            now
        );
        await store.SaveWithEventAsync(item, message, ct);
    }

    private static KitchenStationResponse ToResponse(RestaurantKitchenStation item)
    {
        return new(
            item.Id,
            item.RestaurantId,
            item.Code,
            item.Name,
            item.IsPrimary,
            item.RequiresPrimaryDispatch,
            item.Priority,
            item.IsActive,
            item.Version,
            item.UpdatedAtUtc
        );
    }
}
