namespace Restaurantes.Orders.Application;

public sealed record DiningSessionValidation(int? GuestCount, Guid? ZoneId);

public interface IDiningSessionStore
{
    Task<DiningSessionValidation> EnsureOpenAsync(
        Guid diningSessionId,
        Guid restaurantId,
        Guid? tableId,
        string source,
        string serviceMode,
        string customerAccessToken,
        CancellationToken ct
    );
}
