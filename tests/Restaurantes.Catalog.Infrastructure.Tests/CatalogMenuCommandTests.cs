using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogMenuCommandTests
{
    [Fact]
    public async Task ConfigureMenuItem_returns_null_when_product_or_active_category_is_missing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDatabase();
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        Assert.Null(
            await service.ConfigureMenuItemAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new ConfigureMenuItemRequest { Price = 10m },
                ct
            )
        );

        Category category = Category.Create("PASTA", "Pastas", "CHEF", "Chef", DateTime.UtcNow);
        Product product = Product.Create("P-1", "Pasta", category.Id, DateTime.UtcNow);
        db.Categories.Add(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        category.Update(
            category.Name,
            category.DefaultStationCode,
            category.DefaultStationName,
            false,
            DateTime.UtcNow,
            true,
            true,
            true,
            true
        );

        Assert.Null(
            await service.ConfigureMenuItemAsync(
                Guid.NewGuid(),
                product.Id,
                new ConfigureMenuItemRequest { Price = 10m },
                ct
            )
        );
        Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task ConfigureMenuItem_uses_category_station_for_non_kds_and_publishes_tax_and_event()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDatabase();
        Category category = Category.Create(
            "DRINKS",
            "Bebidas",
            "BAR",
            "Barra",
            DateTime.UtcNow,
            requiresKds: false
        );
        Product product = Product.Create("D-1", "Agua", category.Id, DateTime.UtcNow);
        CatalogTax tax = new()
        {
            Code = "VAT10",
            Name = "IVA 10",
            Rate = 10m,
        };
        product.AssignTax(tax.Id);
        db.Categories.Add(category);
        db.Products.Add(product);
        db.Taxes.Add(tax);
        await db.SaveChangesAsync(ct);
        Guid restaurantId = Guid.NewGuid();
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        var result = await service.ConfigureMenuItemAsync(
            restaurantId,
            product.Id,
            new ConfigureMenuItemRequest
            {
                Price = 2.25m,
                IsAvailable = false,
                PreparationStationCode = "CUSTOM",
                PreparationStationName = "Custom station",
            },
            ct
        );

        Assert.NotNull(result);
        Assert.Equal(2.25m, result.Price);
        Assert.False(result.IsAvailable);
        Assert.False(result.RequiresKds);
        Assert.Equal("", result.PreparationStationCode);
        Assert.Equal("", result.PreparationStationName);
        Assert.Equal(10m, result.TaxRate);
        RestaurantMenuItem saved = Assert.Single(db.MenuItems);
        Assert.Equal("BAR", saved.PreparationStationCode);
        Assert.Equal("Barra", saved.PreparationStationName);
        CatalogItemChanged change = JsonSerializer.Deserialize<CatalogItemChanged>(
            Assert.Single(db.OutboxMessages).Payload
        )!;
        Assert.Equal(product.Id, change.ProductId);
        Assert.Equal(restaurantId, change.RestaurantId);
        Assert.Equal(10m, change.TaxRate);
        Assert.Equal(result.Version, change.Version);
    }

    [Fact]
    public async Task ConfigureMenuItem_requires_active_restaurant_station_for_kds_category()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDatabase();
        Category category = Category.Create("PASTA", "Pastas", "CHEF", "Chef", DateTime.UtcNow);
        Product product = Product.Create("P-1", "Pasta", category.Id, DateTime.UtcNow);
        Guid restaurantId = Guid.NewGuid();
        db.Categories.Add(category);
        db.Products.Add(product);
        db.KitchenStations.Add(
            RestaurantKitchenStation.Create(
                restaurantId,
                "CHEF",
                "Chef",
                new RestaurantKitchenStation.Configuration(false, false, 1),
                DateTime.UtcNow
            )
        );
        await db.SaveChangesAsync(ct);
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        Assert.Null(
            await service.ConfigureMenuItemAsync(
                restaurantId,
                product.Id,
                new ConfigureMenuItemRequest
                {
                    Price = 12m,
                    PreparationStationCode = "MISSING",
                    PreparationStationName = "Missing",
                },
                ct
            )
        );
        db.KitchenStations.Single().SetActive(false, DateTime.UtcNow);
        Assert.Null(
            await service.ConfigureMenuItemAsync(
                restaurantId,
                product.Id,
                new ConfigureMenuItemRequest { Price = 12m },
                ct
            )
        );
        Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task ConfigureMenuItem_updates_existing_item_and_uses_requested_active_station()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDatabase();
        Category category = Category.Create("PASTA", "Pastas", "CHEF", "Chef", DateTime.UtcNow);
        Product product = Product.Create("P-1", "Pasta", category.Id, DateTime.UtcNow);
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation station = RestaurantKitchenStation.Create(
            restaurantId,
            "GRILL",
            "Parrilla",
            new RestaurantKitchenStation.Configuration(false, false, 1),
            DateTime.UtcNow
        );
        RestaurantMenuItem existing = RestaurantMenuItem.Create(
            restaurantId,
            product.Id,
            10m,
            true,
            "CHEF",
            "Chef",
            DateTime.UtcNow
        );
        db.Categories.Add(category);
        db.Products.Add(product);
        db.KitchenStations.Add(station);
        db.MenuItems.Add(existing);
        await db.SaveChangesAsync(ct);
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        var result = await service.ConfigureMenuItemAsync(
            restaurantId,
            product.Id,
            new ConfigureMenuItemRequest
            {
                Price = 14m,
                IsAvailable = false,
                PreparationStationCode = " grill ",
                PreparationStationName = "Ignored name",
            },
            ct
        );

        Assert.NotNull(result);
        Assert.Equal(existing.Id, db.MenuItems.Single().Id);
        Assert.Equal(2, result.Version);
        Assert.Equal("GRILL", result.PreparationStationCode);
        Assert.Equal("Parrilla", result.PreparationStationName);
        Assert.False(result.IsAvailable);
        Assert.Null(result.TaxRate);
    }

    private static CatalogWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("catalog-menu-command-" + Guid.NewGuid().ToString("N"))
                .Options
        );
}
