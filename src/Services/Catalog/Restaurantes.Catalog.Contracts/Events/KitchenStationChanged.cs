namespace Restaurantes.Catalog.Contracts.Events;

public sealed record KitchenStationChanged(
    Guid StationId,
    Guid RestaurantId,
    string Code,
    string Name,
    bool IsPrimary,
    bool RequiresPrimaryDispatch,
    int Priority,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);
