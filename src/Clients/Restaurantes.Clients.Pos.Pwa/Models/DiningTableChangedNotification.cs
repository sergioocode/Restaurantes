namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record DiningTableChangedNotification(
    Guid RestaurantId,
    Guid TableId,
    string Status,
    Guid? ActiveSessionId,
    string Source,
    DateTime OccurredAtUtc
);
