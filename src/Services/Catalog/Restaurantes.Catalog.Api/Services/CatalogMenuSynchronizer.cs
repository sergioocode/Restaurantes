using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;

namespace Restaurantes.Catalog.Api.Services;

public sealed class CatalogMenuSynchronizer(CatalogWriteDbContext db)
{
    public async Task SyncAffectedMenus(Guid restaurantId, CancellationToken ct)
    {
        HashSet<Guid> ids =
            restaurantId == Guid.Empty
                ? (
                    await db
                        .KitchenStations.AsNoTracking()
                        .Select(x => x.RestaurantId)
                        .ToArrayAsync(ct)
                ).ToHashSet()
                : [restaurantId];
        if (restaurantId == Guid.Empty)
        {
            ids.UnionWith(
                await db
                    .MenuItems.AsNoTracking()
                    .Select(x => x.RestaurantId)
                    .Distinct()
                    .ToArrayAsync(ct)
            );
        }
        foreach (Guid id in ids.Where(x => x != Guid.Empty))
            await SyncRestaurantMenu(id, ct);
    }

    private async Task SyncRestaurantMenu(Guid restaurantId, CancellationToken ct)
    {
        RestaurantMenuSyncData data = await LoadRestaurantMenuDataAsync(restaurantId, ct);
        DateTime now = DateTime.UtcNow;
        foreach (Product product in data.Products)
            SyncProductMenuItem(restaurantId, product, data, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task<RestaurantMenuSyncData> LoadRestaurantMenuDataAsync(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        CatalogMenu? menu = await db
            .CatalogMenus.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestaurantId == Guid.Empty && x.IsActive, ct);
        Guid[] enabledProductIds = await db
            .RestaurantProductAvailabilities.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsAvailable)
            .Select(x => x.ProductId)
            .ToArrayAsync(ct);
        CatalogMenuEntry[] entries = menu is null
            ? []
            : await db
                .CatalogMenuEntries.AsNoTracking()
                .Where(x => x.MenuId == menu.Id && enabledProductIds.Contains(x.ProductId))
                .ToArrayAsync(ct);
        CatalogMenuPrice[] prices = menu is null
            ? []
            : await db
                .CatalogMenuPrices.AsNoTracking()
                .Where(x => x.MenuId == menu.Id && x.Channel == "LOCAL")
                .ToArrayAsync(ct);
        RestaurantMenuItem[] existing = await db
            .MenuItems.Where(x => x.RestaurantId == restaurantId)
            .ToArrayAsync(ct);
        ProductOptionGroup[] extraGroups = await db
            .ProductOptionGroups.AsNoTracking()
            .Where(x =>
                x.Kind == "Extra"
                && entries.Select(entry => entry.ProductId).Contains(x.ParentProductId)
            )
            .ToArrayAsync(ct);
        Guid[] groupIds = extraGroups.Select(x => x.Id).ToArray();
        ProductOption[] extraOptions =
            groupIds.Length == 0
                ? []
                : await db
                    .ProductOptions.AsNoTracking()
                    .Where(x => groupIds.Contains(x.GroupId))
                    .ToArrayAsync(ct);
        Guid[] productIds = entries
            .Select(x => x.ProductId)
            .Concat(existing.Select(x => x.ProductId))
            .Concat(extraOptions.Select(x => x.ProductId))
            .Distinct()
            .ToArray();
        Product[] products = await db
            .Products.AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToArrayAsync(ct);
        Dictionary<Guid, decimal> activeTaxRates = await db
            .Taxes.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Id, x => x.Rate, ct);
        Guid[] categoryIds = products.Select(x => x.CategoryId).Distinct().ToArray();
        Category[] categories = await db
            .Categories.AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id))
            .ToArrayAsync(ct);
        Guid[] extraProductIds = extraOptions.Select(x => x.ProductId).Distinct().ToArray();
        CatalogMenuPrice[] extraPrices =
            menu is null || extraProductIds.Length == 0
                ? []
                : await db
                    .CatalogMenuPrices.AsNoTracking()
                    .Where(x =>
                        x.MenuId == menu.Id
                        && x.Channel == "LOCAL"
                        && x.Format == "RACION"
                        && extraProductIds.Contains(x.ProductId)
                    )
                    .ToArrayAsync(ct);
        Dictionary<Guid, decimal> extraPriceByProduct = extraPrices
            .GroupBy(x => x.ProductId)
            .ToDictionary(x => x.Key, x => x.First().Amount);
        return new RestaurantMenuSyncData(
            entries,
            prices,
            existing,
            extraGroups,
            extraOptions,
            products,
            activeTaxRates,
            categories,
            extraPriceByProduct
        );
    }

    private void SyncProductMenuItem(
        Guid restaurantId,
        Product product,
        RestaurantMenuSyncData data,
        DateTime now
    )
    {
        Category? category = data.Categories.FirstOrDefault(x => x.Id == product.CategoryId);
        if (category is null)
            return;
        CatalogMenuEntry? entry = data.Entries.FirstOrDefault(x => x.ProductId == product.Id);
        CatalogMenuPrice? price = entry is null
            ? null
            : data.Prices.FirstOrDefault(x =>
                x.ProductId == product.Id && x.Format == entry.DefaultFormat
            );
        bool available = IsProductAvailable(product, category, price, data.ActiveTaxRates);
        RestaurantMenuItem? item = data.Existing.FirstOrDefault(x => x.ProductId == product.Id);
        if (item is null && !available)
            return;

        item = SaveRestaurantMenuItem(restaurantId, product, category, price, item, available, now);
        decimal? taxRate = GetTaxRate(product, data.ActiveTaxRates);
        CatalogItemChanged changed = new(
            restaurantId,
            product.Id,
            product.Sku,
            product.Name,
            category.Id,
            category.Code,
            category.Name,
            item.Price,
            item.IsAvailable,
            item.PreparationStationCode,
            item.PreparationStationName,
            item.Version,
            now,
            BuildExtraGroups(product.Id, data),
            entry?.DefaultFormat ?? "PLATO",
            data.Prices.Where(x => x.ProductId == product.Id)
                .Select(x => new MenuItemFormatPrice(x.Format, x.Amount))
                .ToArray(),
            RequiresKds: category.RequiresKds,
            TaxRate: taxRate
        );
        db.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = typeof(CatalogItemChanged).FullName!,
                Payload = JsonSerializer.Serialize(changed),
                OccurredAtUtc = now,
            }
        );
    }

    private static bool IsProductAvailable(
        Product product,
        Category category,
        CatalogMenuPrice? price,
        Dictionary<Guid, decimal> activeTaxRates
    )
    {
        return product.IsActive
            && category.IsActive
            && price is not null
            && product.TaxId is Guid taxId
            && activeTaxRates.ContainsKey(taxId);
    }

    private RestaurantMenuItem SaveRestaurantMenuItem(
        Guid restaurantId,
        Product product,
        Category category,
        CatalogMenuPrice? price,
        RestaurantMenuItem? item,
        bool available,
        DateTime now
    )
    {
        string stationCode = item?.PreparationStationCode ?? category.DefaultStationCode;
        string stationName = item?.PreparationStationName ?? category.DefaultStationName;
        if (item is null)
        {
            RestaurantMenuItem created = RestaurantMenuItem.Create(
                restaurantId,
                product.Id,
                price!.Amount,
                true,
                stationCode,
                stationName,
                now
            );
            db.MenuItems.Add(created);
            return created;
        }

        item.Configure(price?.Amount ?? item.Price, available, stationCode, stationName, now);
        return item;
    }

    private static decimal? GetTaxRate(Product product, Dictionary<Guid, decimal> activeTaxRates)
    {
        return product.TaxId is Guid taxId && activeTaxRates.TryGetValue(taxId, out decimal rate)
            ? rate
            : null;
    }

    private static ProductExtraGroup[] BuildExtraGroups(
        Guid parentProductId,
        RestaurantMenuSyncData data
    )
    {
        return data
            .ExtraGroups.Where(group => group.ParentProductId == parentProductId)
            .OrderBy(group => group.SortOrder)
            .Select(group => new ProductExtraGroup(
                group.Id,
                group.Name,
                group.MinSelections,
                group.MaxSelections,
                data.ExtraOptions.Where(option => option.GroupId == group.Id)
                    .Where(option => data.ExtraPriceByProduct.ContainsKey(option.ProductId))
                    .OrderBy(option => option.SortOrder)
                    .Select(option =>
                        data.Products.FirstOrDefault(product => product.Id == option.ProductId)
                    )
                    .Where(product =>
                        product is { IsActive: true }
                        && product.TaxId is Guid taxId
                        && data.ActiveTaxRates.ContainsKey(taxId)
                    )
                    .Select(product => new ProductExtraOption(
                        product!.Id,
                        product.Sku,
                        product.Name,
                        data.ExtraPriceByProduct[product.Id],
                        product.TaxId is Guid taxId ? data.ActiveTaxRates[taxId] : null
                    ))
                    .ToArray()
            ))
            .Where(group => group.Options.Count > 0)
            .ToArray();
    }

    private sealed record RestaurantMenuSyncData(
        CatalogMenuEntry[] Entries,
        CatalogMenuPrice[] Prices,
        RestaurantMenuItem[] Existing,
        ProductOptionGroup[] ExtraGroups,
        ProductOption[] ExtraOptions,
        Product[] Products,
        Dictionary<Guid, decimal> ActiveTaxRates,
        Category[] Categories,
        Dictionary<Guid, decimal> ExtraPriceByProduct
    );
}
