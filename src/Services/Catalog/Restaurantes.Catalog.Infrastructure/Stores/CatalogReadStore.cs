using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;

namespace Restaurantes.Catalog.Infrastructure.Stores;

public sealed class CatalogReadStore(CatalogReadDbContext db, CatalogWriteDbContext writeDb)
    : ICatalogReadStore
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
                x.UpdatedAtUtc,
                x.ShowInPos,
                x.ShowInCommander,
                x.ShowInCustomerQr,
                x.RequiresKds
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken ct)
    {
        return await writeDb
            .Products.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ProductResponse(
                x.Id,
                x.Sku,
                x.Name,
                x.CategoryId,
                x.IsActive,
                x.Version,
                x.UpdatedAtUtc
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MenuItemResponse>> ListMenuAsync(
        Guid restaurantId,
        CatalogClientChannel channel,
        CancellationToken ct
    )
    {
        CatalogMenu? menu = await writeDb
            .CatalogMenus.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestaurantId == Guid.Empty && x.IsActive, ct);
        if (menu is null)
            return [];

        MenuProjectionData data = await LoadMenuProjectionDataAsync(
            restaurantId,
            menu.Id,
            channel,
            ct
        );
        return BuildMenuItems(data);
    }

    private async Task<MenuProjectionData> LoadMenuProjectionDataAsync(
        Guid restaurantId,
        Guid menuId,
        CatalogClientChannel channel,
        CancellationToken ct
    )
    {
        Guid[] enabledProductIds = await writeDb
            .RestaurantProductAvailabilities.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsAvailable)
            .Select(x => x.ProductId)
            .ToArrayAsync(ct);
        CatalogMenuEntry[] entries = await writeDb
            .CatalogMenuEntries.AsNoTracking()
            .Where(x => x.MenuId == menuId && enabledProductIds.Contains(x.ProductId))
            .OrderBy(x => x.SortOrder)
            .ToArrayAsync(ct);
        Guid[] ids = entries.Select(x => x.ProductId).ToArray();
        CatalogMenuPrice[] prices = await writeDb
            .CatalogMenuPrices.AsNoTracking()
            .Where(x => x.MenuId == menuId && x.Channel == "LOCAL" && ids.Contains(x.ProductId))
            .ToArrayAsync(ct);
        var photos = await writeDb
            .ProductPhotos.AsNoTracking()
            .Where(x => ids.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, ct);
        Guid[] taxedIds = await writeDb
            .Products.AsNoTracking()
            .Where(x =>
                ids.Contains(x.Id)
                && x.IsActive
                && x.TaxId != null
                && writeDb.Taxes.Any(t => t.Id == x.TaxId && t.IsActive)
            )
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        var products = await db
            .Products.AsNoTracking()
            .Where(x => taxedIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        Guid[] categoryIds = products.Values.Select(x => x.CategoryId).Distinct().ToArray();
        IQueryable<CategoryReadModel> categoryQuery = db
            .Categories.AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id) && x.IsActive);
        categoryQuery = channel switch
        {
            CatalogClientChannel.Pos => categoryQuery.Where(x => x.ShowInPos),
            CatalogClientChannel.Commander => categoryQuery.Where(x => x.ShowInCommander),
            CatalogClientChannel.CustomerQr => categoryQuery.Where(x => x.ShowInCustomerQr),
            _ => categoryQuery.Where(x => false),
        };
        var categories = await categoryQuery.ToDictionaryAsync(x => x.Id, ct);
        var stations = await db
            .MenuItems.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && ids.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, ct);
        ProductOptionGroup[] groups = await writeDb
            .ProductOptionGroups.AsNoTracking()
            .Where(x => ids.Contains(x.ParentProductId) && x.Kind == "Extra")
            .OrderBy(x => x.SortOrder)
            .ToArrayAsync(ct);
        Guid[] groupIds = groups.Select(x => x.Id).ToArray();
        ProductOption[] options =
            groupIds.Length == 0
                ? []
                : await writeDb
                    .ProductOptions.AsNoTracking()
                    .Where(x => groupIds.Contains(x.GroupId))
                    .ToArrayAsync(ct);
        Guid[] extraIds = options.Select(x => x.ProductId).Distinct().ToArray();
        var extraProducts =
            extraIds.Length == 0
                ? new Dictionary<Guid, ProductReadModel>()
                : await db
                    .Products.AsNoTracking()
                    .Where(x => extraIds.Contains(x.Id) && x.IsActive)
                    .ToDictionaryAsync(x => x.Id, ct);
        CatalogMenuPrice[] extraPrices =
            extraIds.Length == 0
                ? []
                : await writeDb
                    .CatalogMenuPrices.AsNoTracking()
                    .Where(x =>
                        x.MenuId == menuId
                        && x.Channel == "LOCAL"
                        && x.Format == "RACION"
                        && extraIds.Contains(x.ProductId)
                    )
                    .ToArrayAsync(ct);
        var extraPriceByProduct = extraPrices.ToDictionary(x => x.ProductId, x => x.Amount);
        Dictionary<Guid, ProductExtraGroup[]> extraGroupsByParent = BuildExtraGroups(
            groups,
            options,
            extraProducts,
            extraPriceByProduct
        );
        return new MenuProjectionData(
            restaurantId,
            entries,
            prices,
            products,
            categories,
            stations,
            photos,
            extraGroupsByParent
        );
    }

    private static Dictionary<Guid, ProductExtraGroup[]> BuildExtraGroups(
        ProductOptionGroup[] groups,
        ProductOption[] options,
        Dictionary<Guid, ProductReadModel> extraProducts,
        Dictionary<Guid, decimal> extraPriceByProduct
    )
    {
        return groups
            .GroupBy(group => group.ParentProductId)
            .ToDictionary(
                parent => parent.Key,
                parent =>
                    parent
                        .Select(group =>
                        {
                            ProductExtraOption[] groupOptions = options
                                .Where(option => option.GroupId == group.Id)
                                .Where(option => extraProducts.ContainsKey(option.ProductId))
                                .Where(option => extraPriceByProduct.ContainsKey(option.ProductId))
                                .OrderBy(option => option.SortOrder)
                                .Select(option =>
                                {
                                    ProductReadModel extra = extraProducts[option.ProductId];
                                    return new ProductExtraOption(
                                        extra.Id,
                                        extra.Sku,
                                        extra.Name,
                                        extraPriceByProduct[extra.Id]
                                    );
                                })
                                .ToArray();
                            return new ProductExtraGroup(
                                group.Id,
                                group.Name,
                                group.MinSelections,
                                group.MaxSelections,
                                groupOptions
                            );
                        })
                        .Where(group => group.Options.Count > 0)
                        .ToArray()
            );
    }

    private static List<MenuItemResponse> BuildMenuItems(MenuProjectionData data)
    {
        List<MenuItemResponse> result = [];
        foreach (CatalogMenuEntry entry in data.Entries)
        {
            if (
                !data.Products.TryGetValue(entry.ProductId, out ProductReadModel? product)
                || !data.Categories.TryGetValue(product.CategoryId, out CategoryReadModel? category)
            )
                continue;
            CatalogMenuPrice? price = data.Prices.FirstOrDefault(x =>
                x.ProductId == entry.ProductId && x.Format == entry.DefaultFormat
            );
            if (price is null)
                continue;
            result.Add(CreateMenuItemResponse(data, entry, product, category, price));
        }
        return result;
    }

    private static MenuItemResponse CreateMenuItemResponse(
        MenuProjectionData data,
        CatalogMenuEntry entry,
        ProductReadModel product,
        CategoryReadModel category,
        CatalogMenuPrice price
    )
    {
        data.Stations.TryGetValue(entry.ProductId, out MenuItemReadModel? station);
        data.Photos.TryGetValue(entry.ProductId, out ProductPhoto? photo);
        string photoRoot = photo is null ? "" : $"/api/catalog/products/{product.Id}/photos/";
        string photoVersion = photo is null ? "" : $"?v={photo.Revision}";
        return new MenuItemResponse(
            data.RestaurantId,
            product.Id,
            product.Sku,
            product.Name,
            category.Id,
            category.Code,
            category.Name,
            price.Amount,
            true,
            category.RequiresKds
                ? station?.PreparationStationCode ?? category.DefaultStationCode
                : "",
            category.RequiresKds
                ? station?.PreparationStationName ?? category.DefaultStationName
                : "",
            product.Version,
            product.UpdatedAtUtc,
            entry.DefaultFormat,
            photo is null ? "" : $"{photoRoot}thumbnail{photoVersion}",
            photo is null ? "" : $"{photoRoot}card{photoVersion}",
            photo is null ? "" : $"{photoRoot}detail{photoVersion}",
            data.ExtraGroupsByParent.GetValueOrDefault(product.Id, []),
            data.Prices.Where(itemPrice => itemPrice.ProductId == product.Id)
                .Select(itemPrice => new MenuItemFormatPrice(itemPrice.Format, itemPrice.Amount))
                .ToArray(),
            category.RequiresKds
        );
    }

    private sealed record MenuProjectionData(
        Guid RestaurantId,
        CatalogMenuEntry[] Entries,
        CatalogMenuPrice[] Prices,
        Dictionary<Guid, ProductReadModel> Products,
        Dictionary<Guid, CategoryReadModel> Categories,
        Dictionary<Guid, MenuItemReadModel> Stations,
        Dictionary<Guid, ProductPhoto> Photos,
        Dictionary<Guid, ProductExtraGroup[]> ExtraGroupsByParent
    );

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
                x.RequiresKds ? x.PreparationStationCode : "",
                x.RequiresKds ? x.PreparationStationName : "",
                x.Version,
                x.UpdatedAtUtc,
                "PLATO",
                "",
                "",
                "",
                null,
                null,
                x.RequiresKds
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
                x.PrintTicket,
                x.Priority,
                x.IsActive,
                x.Version,
                x.UpdatedAtUtc
            ))
            .ToListAsync(ct);
    }
}
