namespace Restaurantes.RestaurantOperations.Contracts.Responses;

public sealed record RestaurantResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);
