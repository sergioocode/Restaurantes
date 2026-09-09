namespace Restaurantes.Dining.Application;

public sealed record DiningTableChanged(
    Guid RestaurantId,
    Guid TableId,
    string Status,
    Guid? ActiveSessionId,
    string Source,
    DateTime OccurredAtUtc
);

public interface IDiningNotifications
{
    Task TableChanged(DiningTableChanged notification);
}
