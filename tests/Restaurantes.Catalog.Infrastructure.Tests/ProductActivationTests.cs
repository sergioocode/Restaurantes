using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class ProductActivationTests
{
    [Fact]
    public async Task Deactivating_global_product_disables_all_locals_without_reactivating_them_later()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("product-activation-" + Guid.NewGuid().ToString("N"))
                .Options
        );
        Product product = Product.Create("P-1", "Pasta", Guid.NewGuid(), DateTime.UtcNow);
        db.Products.Add(product);
        Guid firstRestaurantId = Guid.NewGuid();
        Guid secondRestaurantId = Guid.NewGuid();
        db.RestaurantProductAvailabilities.AddRange(
            new RestaurantProductAvailability
            {
                RestaurantId = firstRestaurantId,
                ProductId = product.Id,
                IsAvailable = true,
            },
            new RestaurantProductAvailability
            {
                RestaurantId = secondRestaurantId,
                ProductId = product.Id,
                IsAvailable = true,
            }
        );
        await db.SaveChangesAsync(ct);

        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);
        ProductResponse? deactivated = await service.SetProductActiveAsync(product.Id, false, ct);

        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);
        Assert.All(db.RestaurantProductAvailabilities, x => Assert.False(x.IsAvailable));

        product.Update("Pasta nueva", product.CategoryId, DateTime.UtcNow);
        Assert.False(product.IsActive);

        ProductResponse? reactivated = await service.SetProductActiveAsync(product.Id, true, ct);

        Assert.NotNull(reactivated);
        Assert.True(reactivated.IsActive);
        Assert.All(db.RestaurantProductAvailabilities, x => Assert.False(x.IsAvailable));
    }
}
