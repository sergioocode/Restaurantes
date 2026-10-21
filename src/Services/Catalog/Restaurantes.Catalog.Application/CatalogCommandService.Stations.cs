using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Application;

public sealed partial class CatalogCommandService
{
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
            new(
                request.IsPrimary,
                request.RequiresPrimaryDispatch,
                request.Priority,
                request.PrintTicket,
                request.ManagesZones
            ),
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
            new(
                request.IsPrimary,
                request.RequiresPrimaryDispatch,
                request.Priority,
                request.PrintTicket ?? item.PrintTicket,
                request.ManagesZones ?? item.ManagesZones
            ),
            now
        );
        await SaveStationAsync(item, now, ct);
        return ToResponse(item);
    }

    public Task<KitchenStationResponse?> SetStationNameAsync(
        Guid restaurantId,
        Guid stationId,
        string name,
        CancellationToken ct
    ) => ChangeStationAsync(restaurantId, stationId, (item, now) => item.SetName(name, now), ct);

    public Task<KitchenStationResponse?> SetStationPriorityAsync(
        Guid restaurantId,
        Guid stationId,
        int priority,
        CancellationToken ct
    ) =>
        ChangeStationAsync(
            restaurantId,
            stationId,
            (item, now) => item.SetPriority(priority, now),
            ct
        );

    public Task<KitchenStationResponse?> SetStationActiveAsync(
        Guid restaurantId,
        Guid stationId,
        bool active,
        CancellationToken ct
    ) =>
        ChangeStationAsync(restaurantId, stationId, (item, now) => item.SetActive(active, now), ct);

    public Task<KitchenStationResponse?> SetStationPrimaryAsync(
        Guid restaurantId,
        Guid stationId,
        bool primary,
        CancellationToken ct
    ) =>
        ChangeStationAsync(
            restaurantId,
            stationId,
            (item, now) => item.SetPrimary(primary, now),
            ct
        );

    public Task<KitchenStationResponse?> SetStationRequiresPrimaryDispatchAsync(
        Guid restaurantId,
        Guid stationId,
        bool requiresDispatch,
        CancellationToken ct
    ) =>
        ChangeStationAsync(
            restaurantId,
            stationId,
            (item, now) => item.SetRequiresPrimaryDispatch(requiresDispatch, now),
            ct
        );

    public Task<KitchenStationResponse?> SetStationPrintTicketAsync(
        Guid restaurantId,
        Guid stationId,
        bool printTicket,
        CancellationToken ct
    ) =>
        ChangeStationAsync(
            restaurantId,
            stationId,
            (item, now) => item.SetPrintTicket(printTicket, now),
            ct
        );

    public Task<KitchenStationResponse?> SetStationManagesZonesAsync(
        Guid restaurantId,
        Guid stationId,
        bool managesZones,
        CancellationToken ct
    ) =>
        ChangeStationAsync(
            restaurantId,
            stationId,
            (item, now) => item.SetManagesZones(managesZones, now),
            ct
        );

    private async Task<KitchenStationResponse?> ChangeStationAsync(
        Guid restaurantId,
        Guid stationId,
        Action<RestaurantKitchenStation, DateTime> change,
        CancellationToken ct
    )
    {
        RestaurantKitchenStation? item = await store.FindStationAsync(restaurantId, stationId, ct);
        if (item is null)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        change(item, now);
        if (
            item.IsActive
            && item.IsPrimary
            && await store.HasOtherActivePrimaryStationAsync(restaurantId, stationId, ct)
        )
        {
            throw new InvalidOperationException(
                "Only one active primary KDS is allowed per restaurant."
            );
        }

        await SaveStationAsync(item, now, ct);
        return ToResponse(item);
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
            now,
            item.PrintTicket,
            ManagesZones: item.ManagesZones
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
            item.PrintTicket,
            item.Priority,
            item.IsActive,
            item.Version,
            item.UpdatedAtUtc,
            item.ManagesZones
        );
    }
}
