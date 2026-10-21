using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderCatalogStoreTests
{
    [Fact]
    public async Task Find_returns_requested_restaurant_products_with_station_and_catalog_details()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid extraProductId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        db.CatalogItems.AddRange(
            new OrderCatalogItem
            {
                Id = Guid.NewGuid(),
                RestaurantId = restaurantId,
                ProductId = productId,
                ProductName = "Pasta",
                Price = 16m,
                TaxRate = 10m,
                DefaultFormat = "RACIÓN",
                FormatsJson = "[]",
                CategoryId = categoryId,
                CategoryName = "Pastas",
                StationCode = "CHEF",
                StationName = "Chef",
                RequiresKds = true,
                IsAvailable = true,
                ExtraGroupsJson = JsonSerializer.Serialize(
                    new[]
                    {
                        new ProductExtraGroup(
                            groupId,
                            "Añadidos",
                            0,
                            1,
                            [new ProductExtraOption(extraProductId, "E-1", "Queso", 1.5m, 10m)]
                        ),
                    }
                ),
            },
            new OrderCatalogItem
            {
                Id = Guid.NewGuid(),
                RestaurantId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                ProductName = "Other restaurant product",
                StationCode = "CHEF",
            }
        );
        db.KitchenStationConfigurations.Add(
            new OrderKitchenStationConfiguration
            {
                Id = Guid.NewGuid(),
                RestaurantId = restaurantId,
                Code = "CHEF",
                Name = "Chef",
                RequiresPrimaryDispatch = true,
                Priority = 3,
            }
        );
        await db.SaveChangesAsync(ct);

        IReadOnlyDictionary<Guid, OrderCatalogProduct> result = await new OrderCatalogStore(
            db
        ).FindAsync(restaurantId, [productId], ct);

        OrderCatalogProduct item = Assert.Single(result).Value;
        Assert.Equal(productId, item.ProductId);
        Assert.Equal("Pasta", item.ProductName);
        Assert.Equal(16m, item.Price);
        Assert.Equal("CHEF", item.StationCode);
        Assert.True(item.RequiresPrimaryDispatch);
        Assert.Equal(3, item.Priority);
        Assert.True(item.IsAvailable);
        Assert.True(item.RequiresKds);
        Assert.Equal(10m, item.TaxRate);
        OrderExtraGroup extraGroup = Assert.Single(item.ExtraGroups);
        Assert.Equal(groupId, extraGroup.Id);
        OrderExtraOption extra = Assert.Single(extraGroup.Options);
        Assert.Equal(extraProductId, extra.ProductId);
        Assert.Equal("Queso", extra.ProductName);
        Assert.Equal(10m, extra.TaxRate);
        OrderCatalogFormat format = Assert.Single(item.Formats);
        Assert.Equal("RACIÓN", format.Format);
        Assert.Equal(16m, format.Price);
    }

    [Fact]
    public async Task Find_uses_default_format_and_dispatch_values_when_catalog_data_is_missing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        db.CatalogItems.Add(
            new OrderCatalogItem
            {
                Id = Guid.NewGuid(),
                RestaurantId = restaurantId,
                ProductId = productId,
                ProductName = "Soda",
                Price = 2m,
                DefaultFormat = "VASO",
                FormatsJson = "[]",
                StationCode = "BAR",
                ExtraGroupsJson = "[]",
                IsAvailable = false,
                RequiresKds = false,
            }
        );
        await db.SaveChangesAsync(ct);

        OrderCatalogProduct item = Assert
            .Single(await new OrderCatalogStore(db).FindAsync(restaurantId, [productId], ct))
            .Value;

        Assert.False(item.RequiresPrimaryDispatch);
        Assert.Equal(1, item.Priority);
        Assert.False(item.IsAvailable);
        Assert.False(item.RequiresKds);
        Assert.Empty(item.ExtraGroups);
        OrderCatalogFormat format = Assert.Single(item.Formats);
        Assert.Equal("VASO", format.Format);
        Assert.Equal(2m, format.Price);
        Assert.Empty(await new OrderCatalogStore(db).FindAsync(restaurantId, [], ct));
    }

    private static OrderWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseInMemoryDatabase("order-catalog-store-" + Guid.NewGuid().ToString("N"))
                .Options
        );
}
