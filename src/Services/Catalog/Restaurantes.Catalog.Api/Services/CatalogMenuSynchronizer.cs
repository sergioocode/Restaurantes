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
        ProductExtraGroup[] BuildExtraGroups(Guid parentProductId)
        {
            return extraGroups
                .Where(x => x.ParentProductId == parentProductId)
                .OrderBy(x => x.SortOrder)
                .Select(group => new ProductExtraGroup(
                    group.Id,
                    group.Name,
                    group.MinSelections,
                    group.MaxSelections,
                    extraOptions
                        .Where(option => option.GroupId == group.Id)
                        .OrderBy(option => option.SortOrder)
                        .Where(option => extraPriceByProduct.ContainsKey(option.ProductId))
                        .Select(option => products.FirstOrDefault(p => p.Id == option.ProductId))
                        .Where(product =>
                            product is { IsActive: true }
                            && product.TaxId is Guid taxId
                            && activeTaxRates.ContainsKey(taxId)
                        )
                        .Select(product => new ProductExtraOption(
                            product!.Id,
                            product.Sku,
                            product.Name,
                            extraPriceByProduct[product.Id],
                            product.TaxId is Guid taxId ? activeTaxRates[taxId] : null
                        ))
                        .ToArray()
                ))
                .Where(group => group.Options.Count > 0)
                .ToArray();
        }
        DateTime now = DateTime.UtcNow;
        foreach (Product product in products)
        {
            Category? category = categories.FirstOrDefault(x => x.Id == product.CategoryId);
            if (category is null)
                continue;
            CatalogMenuEntry? entry = entries.FirstOrDefault(x => x.ProductId == product.Id);
            CatalogMenuPrice? price = entry is null
                ? null
                : prices.FirstOrDefault(x =>
                    x.ProductId == product.Id && x.Format == entry.DefaultFormat
                );
            bool available =
                product.IsActive
                && category.IsActive
                && price is not null
                && product.TaxId is Guid taxId
                && activeTaxRates.ContainsKey(taxId);
            RestaurantMenuItem? item = existing.FirstOrDefault(x => x.ProductId == product.Id);
            if (item is null && !available)
                continue;
            string stationCode = item?.PreparationStationCode ?? category.DefaultStationCode;
            string stationName = item?.PreparationStationName ?? category.DefaultStationName;
            if (item is null)
            {
                item = RestaurantMenuItem.Create(
                    restaurantId,
                    product.Id,
                    price!.Amount,
                    true,
                    stationCode,
                    stationName,
                    now
                );
                db.MenuItems.Add(item);
            }
            else
                item.Configure(
                    price?.Amount ?? item.Price,
                    available,
                    stationCode,
                    stationName,
                    now
                );
            decimal? taxRate =
                product.TaxId is Guid productTaxId
                && activeTaxRates.TryGetValue(productTaxId, out decimal productTaxRate)
                    ? productTaxRate
                    : null;
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
                BuildExtraGroups(product.Id),
                entry?.DefaultFormat ?? "PLATO",
                prices
                    .Where(x => x.ProductId == product.Id)
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
        await db.SaveChangesAsync(ct);
    }
}
