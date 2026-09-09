namespace Restaurantes.RestaurantOperations.Contracts.Events;

public sealed record RestaurantChanged(
    Guid RestaurantId,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);
