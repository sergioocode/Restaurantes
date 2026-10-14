using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogMenuSelectionTests
{
    [Fact]
    public async Task Global_menu_is_filtered_by_local_product_availability()
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
        Product extra = Product.Create("EXTRA", "Ingrediente", category.Id, now);
        Guid taxId = Guid.NewGuid();
        Guid extraGroupId = Guid.NewGuid();
        CatalogMenu standard = new()
        {
            RestaurantId = Guid.Empty,
            Name = "General",
            IsActive = true,
        };
        write.Categories.Add(category);
        write.Products.Add(product);
        write.Products.Add(extra);
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
        extra.AssignTax(taxId);
        write.ProductOptionGroups.Add(
            new ProductOptionGroup
            {
                Id = extraGroupId,
                ParentProductId = product.Id,
                Name = "Extras",
                Kind = "Extra",
                MaxSelections = 1,
            }
        );
        write.ProductOptions.Add(
            new ProductOption { GroupId = extraGroupId, ProductId = extra.Id }
        );
        write.RestaurantProductAvailabilities.Add(
            new RestaurantProductAvailability
            {
                RestaurantId = restaurantId,
                ProductId = extra.Id,
                IsAvailable = false,
            }
        );
        write.CatalogMenus.Add(standard);
        write.CatalogMenuEntries.Add(
            new CatalogMenuEntry
            {
                MenuId = standard.Id,
                ProductId = product.Id,
                DefaultFormat = "PLATO",
            }
        );
        write.CatalogMenuPrices.Add(
            new CatalogMenuPrice
            {
                MenuId = standard.Id,
                ProductId = product.Id,
                Channel = "LOCAL",
                Format = "PLATO",
                Amount = 10,
            }
        );
        write.CatalogMenuPrices.Add(
            new CatalogMenuPrice
            {
                MenuId = standard.Id,
                ProductId = product.Id,
                Channel = "LOCAL",
                Format = "RACION",
                Amount = 7,
            }
        );
        write.CatalogMenuPrices.Add(
            new CatalogMenuPrice
            {
                MenuId = standard.Id,
                ProductId = extra.Id,
                Channel = "LOCAL",
                Format = "RACION",
                Amount = 2,
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
                ShowInCommander = false,
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
        read.Products.Add(
            new ProductReadModel
            {
                Id = extra.Id,
                Sku = extra.Sku,
                Name = extra.Name,
                CategoryId = category.Id,
                IsActive = true,
            }
        );
        await write.SaveChangesAsync(ct);
        await read.SaveChangesAsync(ct);

        CatalogReadStore store = new(read, write);
        Assert.Empty(await store.ListMenuAsync(restaurantId, CatalogClientChannel.Pos, ct));

        write.RestaurantProductAvailabilities.Add(
            new RestaurantProductAvailability
            {
                RestaurantId = restaurantId,
                ProductId = product.Id,
                IsAvailable = true,
            }
        );
        await write.SaveChangesAsync(ct);
        var selected = Assert.Single(
            await store.ListMenuAsync(restaurantId, CatalogClientChannel.Pos, ct)
        );
        Assert.Empty(await store.ListMenuAsync(restaurantId, CatalogClientChannel.Commander, ct));
        Assert.Equal(10, selected.Price);
        Assert.Equal("PLATO", selected.DefaultFormat);
        Assert.Contains(selected.Formats!, x => x.Format == "PLATO" && x.Price == 10);
        Assert.Contains(selected.Formats!, x => x.Format == "RACION" && x.Price == 7);
        Assert.Equal(2, Assert.Single(Assert.Single(selected.ExtraGroups!).Options).Price);

        product.SetActive(false, DateTime.UtcNow);
        await write.SaveChangesAsync(ct);
        Assert.Empty(await store.ListMenuAsync(restaurantId, CatalogClientChannel.Pos, ct));

        product.SetActive(true, DateTime.UtcNow);
        await write.SaveChangesAsync(ct);
        Assert.Single(await store.ListMenuAsync(restaurantId, CatalogClientChannel.Pos, ct));

        write.RestaurantProductAvailabilities.Single(x => x.ProductId == product.Id).IsAvailable =
            false;
        await write.SaveChangesAsync(ct);
        Assert.Empty(await store.ListMenuAsync(restaurantId, CatalogClientChannel.Pos, ct));

        write.RestaurantProductAvailabilities.Single(x => x.ProductId == product.Id).IsAvailable =
            true;
        product.AssignTax(null);
        await write.SaveChangesAsync(ct);
        Assert.Empty(await store.ListMenuAsync(restaurantId, CatalogClientChannel.Pos, ct));
    }
}
