using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Infrastructure.Stores;

public sealed class OrderCatalogStore(OrderWriteDbContext dbContext) : IOrderCatalogStore
{
    public async Task<IReadOnlyDictionary<Guid, OrderCatalogProduct>> FindAsync(
        Guid restaurantId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken
    )
    {
        List<OrderCatalogItem> items = await dbContext
            .CatalogItems.AsNoTracking()
            .Where(item => item.RestaurantId == restaurantId && productIds.Contains(item.ProductId))
            .ToListAsync(cancellationToken);
        string[] stationCodes = items.Select(item => item.StationCode).Distinct().ToArray();
        Dictionary<string, OrderKitchenStationConfiguration> configurations = await dbContext
            .KitchenStationConfigurations.AsNoTracking()
            .Where(item => item.RestaurantId == restaurantId && stationCodes.Contains(item.Code))
            .ToDictionaryAsync(item => item.Code, cancellationToken);
        return items.ToDictionary(
            item => item.ProductId,
            item =>
            {
                configurations.TryGetValue(
                    item.StationCode,
                    out OrderKitchenStationConfiguration? configuration
                );
                return new OrderCatalogProduct(
                    item.RestaurantId,
                    item.ProductId,
                    item.ProductName,
                    item.Price,
                    item.CategoryId,
                    item.CategoryName,
                    item.StationCode,
                    item.StationName,
                    configuration?.RequiresPrimaryDispatch ?? false,
                    configuration?.Priority ?? 1,
                    item.IsAvailable,
                    MapExtraGroups(item.ExtraGroupsJson),
                    item.DefaultFormat,
                    MapFormats(item.FormatsJson, item.DefaultFormat, item.Price),
                    item.RequiresKds,
                    item.TaxRate
                );
            }
        );
    }

    private static OrderExtraGroup[] MapExtraGroups(string json)
    {
        ProductExtraGroup[] groups = JsonSerializer.Deserialize<ProductExtraGroup[]>(json) ?? [];
        return groups
            .Select(group => new OrderExtraGroup(
                group.Id,
                group.Name,
                group.MinSelections,
                group.MaxSelections,
                group
                    .Options.Select(option => new OrderExtraOption(
                        option.ProductId,
                        option.ProductName,
                        option.Price,
                        option.TaxRate
                    ))
                    .ToArray()
            ))
            .ToArray();
    }

    private static OrderCatalogFormat[] MapFormats(
        string json,
        string defaultFormat,
        decimal defaultPrice
    )
    {
        OrderCatalogFormat[] formats = JsonSerializer.Deserialize<OrderCatalogFormat[]>(json) ?? [];
        return formats.Length == 0
            ? [new OrderCatalogFormat(defaultFormat, defaultPrice)]
            : formats;
    }
}
