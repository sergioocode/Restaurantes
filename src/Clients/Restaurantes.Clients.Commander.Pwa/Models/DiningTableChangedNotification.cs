namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record DiningTableChangedNotification(
    Guid RestaurantId,
    Guid TableId,
    string Status,
    Guid? ActiveSessionId,
    string Source,
    DateTime OccurredAtUtc
);
