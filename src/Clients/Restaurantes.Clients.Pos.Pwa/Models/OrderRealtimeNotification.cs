namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record OrderRealtimeNotification(
    Guid OrderId,
    Guid RestaurantId,
    string Status,
    int Version,
    DateTime OccurredAtUtc,
    IReadOnlyList<string> StationCodes
);
