using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;

namespace Restaurantes.Catalog.Infrastructure.Stores;

public sealed class CatalogReadStore(CatalogReadDbContext db) : ICatalogReadStore
{
    public async Task<IReadOnlyList<CategoryResponse>> ListCategoriesAsync(CancellationToken ct)
    {
        return await db
            .Categories.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CategoryResponse(
                x.Id,
                x.Code,
                x.Name,
                x.DefaultStationCode,
                x.DefaultStationName,
                x.IsActive,
                x.Version,
                x.UpdatedAtUtc
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken ct)
    {
        return await db
            .Products.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ProductResponse(
                x.Id,
                x.Sku,
                x.Name,
                x.CategoryId,
                x.BasePrice,
                x.IsActive,
                x.Version,
                x.UpdatedAtUtc
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MenuItemResponse>> ListMenuAsync(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return await db
            .MenuItems.AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId
                && x.IsAvailable
                && db.Products.Any(product => product.Id == x.ProductId && product.IsActive)
                && db.Categories.Any(category => category.Id == x.CategoryId && category.IsActive)
            )
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .Select(x => new MenuItemResponse(
                x.RestaurantId,
                x.ProductId,
                x.Sku,
                x.ProductName,
                x.CategoryId,
                x.CategoryCode,
                x.CategoryName,
                x.Price,
                x.IsAvailable,
                x.PreparationStationCode,
                x.PreparationStationName,
                x.Version,
                x.UpdatedAtUtc
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MenuItemResponse>> ListMenuConfigurationAsync(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return await db
            .MenuItems.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .OrderBy(x => x.CategoryName)
            .ThenBy(x => x.ProductName)
            .Select(x => new MenuItemResponse(
                x.RestaurantId,
                x.ProductId,
                x.Sku,
                x.ProductName,
                x.CategoryId,
                x.CategoryCode,
                x.CategoryName,
                x.Price,
                x.IsAvailable,
                x.PreparationStationCode,
                x.PreparationStationName,
                x.Version,
                x.UpdatedAtUtc
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KitchenStationResponse>> ListStationsAsync(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return await db
            .KitchenStations.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .OrderBy(x => x.Name)
            .Select(x => new KitchenStationResponse(
                x.Id,
                x.RestaurantId,
                x.Code,
                x.Name,
                x.IsPrimary,
                x.RequiresPrimaryDispatch,
                x.Priority,
                x.IsActive,
                x.Version,
                x.UpdatedAtUtc
            ))
            .ToListAsync(ct);
    }
}
