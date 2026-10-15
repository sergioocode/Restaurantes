namespace Restaurantes.Orders.Contracts.Events;

public sealed record KdsOrderUpdated(
    Guid OrderId,
    Guid RestaurantId,
    string Source,
    string Status,
    int Version,
    DateTime OccurredAtUtc,
    IReadOnlyList<string> StationCodes
);
