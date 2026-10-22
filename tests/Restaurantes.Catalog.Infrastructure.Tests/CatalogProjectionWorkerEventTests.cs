using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogProjectionWorkerEventTests
{
    [Fact]
    public async Task CategoryChanged_updates_category_and_existing_menu_items()
    {
        await using CatalogReadDbContext db = CreateDb();
        Guid categoryId = Guid.NewGuid();
        MenuItemReadModel menuItem = new()
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            CategoryCode = "old",
            CategoryName = "Old",
            RequiresKds = false,
        };
        db.MenuItems.Add(menuItem);
        CategoryChanged message = new(
            categoryId,
            "food",
            "Food",
            "hot",
            "Hot",
            true,
            1,
            DateTime.UtcNow,
            RequiresKds: true
        );

        await ApplyAsync(db, message);

        CategoryReadModel category = await db.Categories.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("food", category.Code);
        Assert.Equal("Hot", category.DefaultStationName);
        Assert.Equal("food", menuItem.CategoryCode);
        Assert.Equal("Food", menuItem.CategoryName);
        Assert.True(menuItem.RequiresKds);
    }

    [Fact]
    public async Task CategoryChanged_updates_newer_category_versions_and_ignores_stale_events()
    {
        await using CatalogReadDbContext db = CreateDb();
        Guid categoryId = Guid.NewGuid();
        CategoryReadModel category = new()
        {
            Id = categoryId,
            Code = "old",
            Name = "Old",
            Version = 2,
        };
        MenuItemReadModel menuItem = new()
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            CategoryCode = "old",
            CategoryName = "Old",
        };
        db.AddRange(category, menuItem);
        CategoryChanged newer = new(
            categoryId,
            "food",
            "Food",
            "hot",
            "Hot line",
            true,
            3,
            DateTime.UtcNow,
            ShowInPos: false,
            ShowInCommander: true,
            ShowInCustomerQr: false,
            RequiresKds: false
        );

        await ApplyAsync(db, newer);
        await ApplyAsync(db, newer with { Name = "Stale", Version = 1 });

        Assert.Equal("Food", category.Name);
        Assert.Equal("food", category.Code);
        Assert.Equal(3, category.Version);
        Assert.False(category.ShowInPos);
        Assert.True(category.ShowInCommander);
        Assert.False(category.ShowInCustomerQr);
        Assert.False(category.RequiresKds);
        Assert.Equal("Food", menuItem.CategoryName);
        Assert.Equal("food", menuItem.CategoryCode);
        Assert.False(menuItem.RequiresKds);
    }

    [Fact]
    public async Task ProductChanged_updates_product_and_existing_menu_items()
    {
        await using CatalogReadDbContext db = CreateDb();
        Guid categoryId = Guid.NewGuid();
        CategoryReadModel category = new()
        {
            Id = categoryId,
            Code = "drinks",
            Name = "Drinks",
        };
        ProductReadModel product = new() { Id = Guid.NewGuid(), Version = 1 };
        MenuItemReadModel menuItem = new() { Id = Guid.NewGuid(), ProductId = product.Id };
        db.Categories.Add(category);
        db.Products.Add(product);
        db.MenuItems.Add(menuItem);
        ProductChanged message = new(
            product.Id,
            "sku-1",
            "Tea",
            categoryId,
            true,
            2,
            DateTime.UtcNow
        );

        await ApplyAsync(db, message);

        Assert.Equal("Tea", product.Name);
        Assert.Equal(2, product.Version);
        Assert.Equal("sku-1", menuItem.Sku);
        Assert.Equal(categoryId, menuItem.CategoryId);
        Assert.Equal("drinks", menuItem.CategoryCode);
        Assert.Equal("Drinks", menuItem.CategoryName);
    }

    [Fact]
    public async Task ProductChanged_creates_product_and_updates_menu_without_a_category_projection()
    {
        await using CatalogReadDbContext db = CreateDb();
        Guid productId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        MenuItemReadModel menuItem = new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            CategoryCode = "existing",
            CategoryName = "Existing",
        };
        db.MenuItems.Add(menuItem);
        ProductChanged message = new(
            productId,
            "sku-2",
            "Juice",
            categoryId,
            true,
            1,
            DateTime.UtcNow
        );

        await ApplyAsync(db, message);

        ProductReadModel product = await db.Products.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("sku-2", product.Sku);
        Assert.Equal("Juice", product.Name);
        Assert.Equal(categoryId, product.CategoryId);
        Assert.True(product.IsActive);
        Assert.Equal(1, product.Version);
        Assert.Equal("sku-2", menuItem.Sku);
        Assert.Equal("Juice", menuItem.ProductName);
        Assert.Equal(categoryId, menuItem.CategoryId);
        Assert.Equal("existing", menuItem.CategoryCode);
        Assert.Equal("Existing", menuItem.CategoryName);
    }

    [Fact]
    public async Task CatalogItemChanged_creates_updates_and_ignores_older_versions()
    {
        await using CatalogReadDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        CatalogItemChanged first = CatalogItem(restaurantId, productId, "Soup", 4, 1);

        await ApplyAsync(db, first);
        CatalogItemChanged newer = CatalogItem(restaurantId, productId, "Soup of day", 5, 2);
        await ApplyAsync(db, newer);
        await ApplyAsync(db, first);

        MenuItemReadModel item = await db.MenuItems.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("Soup of day", item.ProductName);
        Assert.Equal(5, item.Price);
        Assert.Equal(2, item.Version);
    }

    [Fact]
    public async Task KitchenStationChanged_updates_menu_and_deletes_only_newer_station_versions()
    {
        await using CatalogReadDbContext db = CreateDb();
        Guid stationId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        KitchenStationChanged first = Station(stationId, restaurantId, "Hot line", 1);
        MenuItemReadModel item = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            PreparationStationCode = "hot",
        };
        db.MenuItems.Add(item);

        await ApplyAsync(db, first);
        Assert.Equal("Hot line", item.PreparationStationName);

        await ApplyAsync(db, Station(stationId, restaurantId, "Hot line 2", 2));
        await ApplyAsync(db, Station(stationId, restaurantId, "Old name", 1));
        Assert.Equal(
            "Hot line 2",
            (await db.KitchenStations.SingleAsync(TestContext.Current.CancellationToken)).Name
        );

        await ApplyAsync(db, Station(stationId, restaurantId, "Hot line", 1, isDeleted: true));
        Assert.Single(await db.KitchenStations.ToListAsync(TestContext.Current.CancellationToken));
        await ApplyAsync(db, Station(stationId, restaurantId, "Hot line", 3, isDeleted: true));
        Assert.Empty(await db.KitchenStations.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static CatalogReadDbContext CreateDb()
    {
        DbContextOptions<CatalogReadDbContext> options =
            new DbContextOptionsBuilder<CatalogReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new CatalogReadDbContext(options);
    }

    private static CatalogItemChanged CatalogItem(
        Guid restaurantId,
        Guid productId,
        string name,
        decimal price,
        int version
    )
    {
        return new CatalogItemChanged(
            restaurantId,
            productId,
            "sku",
            name,
            Guid.NewGuid(),
            "food",
            "Food",
            price,
            true,
            "hot",
            "Hot",
            version,
            DateTime.UtcNow
        );
    }

    private static KitchenStationChanged Station(
        Guid stationId,
        Guid restaurantId,
        string name,
        int version,
        bool isDeleted = false
    )
    {
        return new KitchenStationChanged(
            stationId,
            restaurantId,
            "hot",
            name,
            false,
            false,
            0,
            true,
            version,
            DateTime.UtcNow,
            IsDeleted: isDeleted
        );
    }

    private static async Task ApplyAsync<T>(CatalogReadDbContext db, T message)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message);
        await CatalogProjectionWorker.ApplyEventAsync(db, typeof(T).FullName!, body);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unknown_catalog_event_is_rejected()
    {
        await using CatalogReadDbContext db = CreateDb();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            CatalogProjectionWorker.ApplyEventAsync(db, "unknown.event", "{}"u8.ToArray())
        );
    }
}
