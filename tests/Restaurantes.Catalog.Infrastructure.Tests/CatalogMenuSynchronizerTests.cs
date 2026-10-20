using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Api.Services;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogMenuSynchronizerTests
{
    [Fact]
    public async Task Sync_creates_available_menu_item_and_publishes_complete_catalog_event()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        MenuData data = AddMenuData(db, withExtra: true);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new CatalogMenuSynchronizer(db).SyncAffectedMenus(
            data.RestaurantId,
            TestContext.Current.CancellationToken
        );

        RestaurantMenuItem item = Assert.Single(db.MenuItems);
        Assert.Equal(data.Product.Id, item.ProductId);
        Assert.Equal(data.RestaurantId, item.RestaurantId);
        Assert.Equal(12, item.Price);
        Assert.True(item.IsAvailable);
        Assert.Equal("KITCHEN", item.PreparationStationCode);
        CatalogItemChanged changed = ReadEvent(Assert.Single(db.OutboxMessages));
        Assert.Equal(item.Id, db.MenuItems.Single().Id);
        Assert.Equal(data.TaxRate, changed.TaxRate);
        Assert.Equal("PLATO", changed.DefaultFormat);
        Assert.Contains(changed.Formats!, x => x.Format == "PLATO" && x.Price == 12);
        Assert.Contains(changed.Formats!, x => x.Format == "RACION" && x.Price == 8);
        ProductExtraGroup group = Assert.Single(changed.ExtraGroups!);
        Assert.Equal("Extras", group.Name);
        ProductExtraOption option = Assert.Single(group.Options);
        Assert.Equal(data.ExtraProduct!.Id, option.ProductId);
        Assert.Equal(3, option.Price);
        Assert.Equal(data.TaxRate, option.TaxRate);
        Assert.True(changed.RequiresKds);
    }

    [Fact]
    public async Task Sync_marks_existing_item_unavailable_when_active_menu_price_is_missing()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        MenuData data = AddMenuData(db, withExtra: false, includePrice: false);
        RestaurantMenuItem existing = RestaurantMenuItem.Create(
            data.RestaurantId,
            data.Product.Id,
            9,
            true,
            "BAR",
            "Barra",
            DateTime.UtcNow.AddMinutes(-1)
        );
        db.MenuItems.Add(existing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new CatalogMenuSynchronizer(db).SyncAffectedMenus(
            data.RestaurantId,
            TestContext.Current.CancellationToken
        );

        Assert.Same(existing, Assert.Single(db.MenuItems));
        Assert.False(existing.IsAvailable);
        Assert.Equal(9, existing.Price);
        Assert.Equal("BAR", existing.PreparationStationCode);
        CatalogItemChanged changed = ReadEvent(Assert.Single(db.OutboxMessages));
        Assert.False(changed.IsAvailable);
        Assert.Equal(9, changed.Price);
        Assert.Empty(changed.Formats!);
    }

    [Fact]
    public async Task Sync_skips_new_items_without_an_active_tax_or_category()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        MenuData data = AddMenuData(db, withExtra: false);
        data.Product.AssignTax(null);
        Product missingCategoryProduct = Product.Create(
            "MISSING",
            "Sin categoría",
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        db.Products.Add(missingCategoryProduct);
        db.CatalogMenuEntries.Add(
            new CatalogMenuEntry
            {
                MenuId = data.Menu.Id,
                ProductId = missingCategoryProduct.Id,
                DefaultFormat = "PLATO",
            }
        );
        db.CatalogMenuPrices.Add(
            new CatalogMenuPrice
            {
                MenuId = data.Menu.Id,
                ProductId = missingCategoryProduct.Id,
                Channel = "LOCAL",
                Format = "PLATO",
                Amount = 5,
            }
        );
        db.RestaurantProductAvailabilities.Add(
            new RestaurantProductAvailability
            {
                RestaurantId = data.RestaurantId,
                ProductId = missingCategoryProduct.Id,
                IsAvailable = true,
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new CatalogMenuSynchronizer(db).SyncAffectedMenus(
            data.RestaurantId,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(db.MenuItems);
        Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task Sync_all_restaurants_uses_station_and_existing_item_restaurant_ids()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        MenuData data = AddMenuData(db, withExtra: false);
        Guid existingRestaurant = Guid.NewGuid();
        RestaurantMenuItem existing = RestaurantMenuItem.Create(
            existingRestaurant,
            data.Product.Id,
            7,
            true,
            "BAR",
            "Barra",
            DateTime.UtcNow.AddMinutes(-1)
        );
        db.MenuItems.Add(existing);
        db.KitchenStations.Add(
            RestaurantKitchenStation.Create(
                data.RestaurantId,
                "MAIN",
                "Cocina",
                new RestaurantKitchenStation.Configuration(true, false, 0),
                DateTime.UtcNow
            )
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new CatalogMenuSynchronizer(db).SyncAffectedMenus(
            Guid.Empty,
            TestContext.Current.CancellationToken
        );

        RestaurantMenuItem created = Assert.Single(
            db.MenuItems.Where(x => x.RestaurantId == data.RestaurantId)
        );
        RestaurantMenuItem retained = Assert.Single(
            db.MenuItems.Where(x => x.RestaurantId == existingRestaurant)
        );
        Assert.True(created.IsAvailable);
        Assert.False(retained.IsAvailable);
        Assert.Equal(2, db.OutboxMessages.Count());
    }

    private static MenuData AddMenuData(
        CatalogWriteDbContext db,
        bool withExtra,
        bool includePrice = true
    )
    {
        DateTime now = DateTime.UtcNow;
        Guid restaurantId = Guid.NewGuid();
        Category category = Category.Create("MAIN", "Principales", "KITCHEN", "Cocina", now);
        Product product = Product.Create("PASTA", "Pasta", category.Id, now);
        Guid taxId = Guid.NewGuid();
        const decimal taxRate = 10;
        product.AssignTax(taxId);
        CatalogMenu menu = new()
        {
            RestaurantId = Guid.Empty,
            Name = "Carta general",
            IsActive = true,
        };
        Product? extraProduct = withExtra
            ? Product.Create("EXTRA", "Ingrediente", category.Id, now)
            : null;
        if (extraProduct is not null)
            extraProduct.AssignTax(taxId);

        db.Categories.Add(category);
        db.Products.Add(product);
        if (extraProduct is not null)
            db.Products.Add(extraProduct);
        db.Taxes.Add(
            new CatalogTax
            {
                Id = taxId,
                Code = "IVA",
                Name = "IVA",
                Rate = taxRate,
            }
        );
        db.CatalogMenus.Add(menu);
        db.CatalogMenuEntries.Add(
            new CatalogMenuEntry
            {
                MenuId = menu.Id,
                ProductId = product.Id,
                DefaultFormat = "PLATO",
            }
        );
        if (includePrice)
        {
            db.CatalogMenuPrices.Add(
                new CatalogMenuPrice
                {
                    MenuId = menu.Id,
                    ProductId = product.Id,
                    Channel = "LOCAL",
                    Format = "PLATO",
                    Amount = 12,
                }
            );
            db.CatalogMenuPrices.Add(
                new CatalogMenuPrice
                {
                    MenuId = menu.Id,
                    ProductId = product.Id,
                    Channel = "LOCAL",
                    Format = "RACION",
                    Amount = 8,
                }
            );
        }
        db.RestaurantProductAvailabilities.Add(
            new RestaurantProductAvailability
            {
                RestaurantId = restaurantId,
                ProductId = product.Id,
                IsAvailable = true,
            }
        );
        if (extraProduct is not null)
        {
            Guid groupId = Guid.NewGuid();
            db.ProductOptionGroups.Add(
                new ProductOptionGroup
                {
                    Id = groupId,
                    ParentProductId = product.Id,
                    Name = "Extras",
                    Kind = "Extra",
                    MaxSelections = 2,
                    SortOrder = 1,
                }
            );
            db.ProductOptions.Add(
                new ProductOption { GroupId = groupId, ProductId = extraProduct.Id }
            );
            db.CatalogMenuPrices.Add(
                new CatalogMenuPrice
                {
                    MenuId = menu.Id,
                    ProductId = extraProduct.Id,
                    Channel = "LOCAL",
                    Format = "RACION",
                    Amount = 3,
                }
            );
        }

        return new MenuData(restaurantId, menu, product, extraProduct!, taxRate);
    }

    private static CatalogWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("catalog-sync-" + Guid.NewGuid().ToString("N"))
                .Options
        );

    private static CatalogItemChanged ReadEvent(OutboxMessage message) =>
        JsonSerializer.Deserialize<CatalogItemChanged>(message.Payload)!;

    private sealed record MenuData(
        Guid RestaurantId,
        CatalogMenu Menu,
        Product Product,
        Product? ExtraProduct,
        decimal TaxRate
    );
}
