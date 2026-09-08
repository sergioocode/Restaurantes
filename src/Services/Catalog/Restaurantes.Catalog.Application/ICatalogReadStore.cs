using Restaurantes.Catalog.Contracts;

namespace Restaurantes.Catalog.Application;

public interface ICatalogReadStore
{
    Task<IReadOnlyList<CategoryResponse>> ListCategoriesAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken ct);
    Task<IReadOnlyList<MenuItemResponse>> ListMenuAsync(Guid restaurantId, CancellationToken ct);
    Task<IReadOnlyList<MenuItemResponse>> ListMenuConfigurationAsync(
        Guid restaurantId,
        CancellationToken ct
    );
    Task<IReadOnlyList<KitchenStationResponse>> ListStationsAsync(
        Guid restaurantId,
        CancellationToken ct
    );
}
