namespace Restaurantes.Orders.Application;

public interface IOrderCatalogStore
{
    Task<IReadOnlyDictionary<Guid, OrderCatalogProduct>> FindAsync(
        Guid restaurantId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct
    );
}
