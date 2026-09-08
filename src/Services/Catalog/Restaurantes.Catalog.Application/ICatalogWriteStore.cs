using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Application;

public interface ICatalogWriteStore
{
    Task<bool> CategoryCodeExistsAsync(string code, CancellationToken ct);
    Task<bool> ProductSkuExistsAsync(string sku, CancellationToken ct);
    Task<Category?> FindCategoryAsync(Guid id, CancellationToken ct);
    Task<Product?> FindProductAsync(Guid id, CancellationToken ct);
    Task<RestaurantMenuItem?> FindMenuItemAsync(
        Guid restaurantId,
        Guid productId,
        CancellationToken ct
    );
    Task<RestaurantKitchenStation?> FindStationAsync(
        Guid restaurantId,
        Guid id,
        CancellationToken ct
    );
    Task<RestaurantKitchenStation?> FindStationByCodeAsync(
        Guid restaurantId,
        string code,
        CancellationToken ct
    );
    Task<bool> HasOtherActivePrimaryStationAsync(
        Guid restaurantId,
        Guid? excludedStationId,
        CancellationToken ct
    );
    Task SaveWithEventAsync(object aggregate, object integrationEvent, CancellationToken ct);
}
