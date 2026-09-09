namespace Restaurantes.Catalog.Contracts.Responses;

public sealed record KitchenStationResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Name,
    bool IsPrimary,
    bool RequiresPrimaryDispatch,
    int Priority,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);
