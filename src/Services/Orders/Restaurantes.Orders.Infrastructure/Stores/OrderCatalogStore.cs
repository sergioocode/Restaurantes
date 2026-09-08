using Microsoft.EntityFrameworkCore;
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
                    item.IsAvailable
                );
            }
        );
    }
}
