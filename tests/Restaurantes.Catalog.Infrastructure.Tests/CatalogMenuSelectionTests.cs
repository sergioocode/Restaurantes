using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogMenuSelectionTests
{
    [Fact]
    public async Task Local_active_menu_overrides_default_and_inactive_local_uses_default()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid restaurantId = Guid.NewGuid();
        string suffix = Guid.NewGuid().ToString("N");
        await using CatalogWriteDbContext write = new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("catalog-write-" + suffix)
                .Options
        );
        await using CatalogReadDbContext read = new(
            new DbContextOptionsBuilder<CatalogReadDbContext>()
                .UseInMemoryDatabase("catalog-read-" + suffix)
                .Options
        );
        DateTime now = DateTime.UtcNow;
        Category category = Category.Create("MAIN", "Principales", "GENERAL", "General", now);
        Product product = Product.Create("PASTA", "Pasta", category.Id, now);
        Guid taxId = Guid.NewGuid();
        CatalogMenu standard = new() { RestaurantId = Guid.Empty, Name = "General" };
        CatalogMenu local = new()
        {
            RestaurantId = restaurantId,
            Name = "Cena",
            IsActive = true,
        };
        write.Categories.Add(category);
        write.Products.Add(product);
        write.Taxes.Add(
            new CatalogTax
            {
                Id = taxId,
                Code = "IVA",
                Name = "IVA",
                Rate = 10,
            }
        );
        product.AssignTax(taxId);
        write.CatalogMenus.AddRange(standard, local);
        write.CatalogMenuEntries.AddRange(
            new CatalogMenuEntry
            {
                MenuId = standard.Id,
                ProductId = product.Id,
                DefaultFormat = "PLATO",
            },
            new CatalogMenuEntry
            {
                MenuId = local.Id,
                ProductId = product.Id,
                DefaultFormat = "RACION",
            }
        );
        write.CatalogMenuPrices.AddRange(
            new CatalogMenuPrice
            {
                MenuId = standard.Id,
                ProductId = product.Id,
                Channel = "LOCAL",
                Format = "PLATO",
                Amount = 10,
            },
            new CatalogMenuPrice
            {
                MenuId = local.Id,
                ProductId = product.Id,
                Channel = "LOCAL",
                Format = "RACION",
                Amount = 14,
            }
        );
        read.Categories.Add(
            new CategoryReadModel
            {
                Id = category.Id,
                Code = category.Code,
                Name = category.Name,
                DefaultStationCode = category.DefaultStationCode,
                DefaultStationName = category.DefaultStationName,
                IsActive = true,
            }
        );
        read.Products.Add(
            new ProductReadModel
            {
                Id = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                CategoryId = category.Id,
                IsActive = true,
            }
        );
        await write.SaveChangesAsync(ct);
        await read.SaveChangesAsync(ct);

        CatalogReadStore store = new(read, write);
        var selected = Assert.Single(await store.ListMenuAsync(restaurantId, ct));
        Assert.Equal(14, selected.Price);
        Assert.Equal("RACION", selected.DefaultFormat);

        local.IsActive = false;
        await write.SaveChangesAsync(ct);
        var fallback = Assert.Single(await store.ListMenuAsync(restaurantId, ct));
        Assert.Equal(10, fallback.Price);
        Assert.Equal("PLATO", fallback.DefaultFormat);

        product.AssignTax(null);
        await write.SaveChangesAsync(ct);
        Assert.Empty(await store.ListMenuAsync(restaurantId, ct));
    }
}
