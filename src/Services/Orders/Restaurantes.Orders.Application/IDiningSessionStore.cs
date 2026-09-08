namespace Restaurantes.Orders.Application;

public interface IDiningSessionStore
{
    Task<int?> EnsureOpenAsync(
        Guid diningSessionId,
        Guid restaurantId,
        Guid tableId,
        string source,
        string serviceMode,
        string customerAccessToken,
        CancellationToken ct
    );
}
