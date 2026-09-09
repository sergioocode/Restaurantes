namespace Restaurantes.Orders.Consumer.Handlers;

public sealed record KdsProjectionNotification(
    Guid OrderId,
    Guid RestaurantId,
    string Status,
    int Version,
    IReadOnlyList<string> AffectedStationCodes
);
