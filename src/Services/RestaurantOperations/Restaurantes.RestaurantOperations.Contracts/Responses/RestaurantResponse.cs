namespace Restaurantes.RestaurantOperations.Contracts.Responses;

public sealed record RestaurantResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    Guid? CompanyId,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);
