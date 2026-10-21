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

public sealed class CategoryUpdateTests
{
    [Fact]
    public async Task Updating_category_persists_channel_visibility_and_publishes_the_change()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("category-update-" + Guid.NewGuid().ToString("N"))
                .Options
        );
        Category category = Category.Create("DRINKS", "Bebidas", "BAR", "Bar", DateTime.UtcNow);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        var result = await service.UpdateCategoryAsync(
            category.Id,
            new UpdateCategoryRequest
            {
                Name = "Bebidas frías",
                DefaultStationCode = "BAR",
                DefaultStationName = "Barra",
                IsActive = true,
                ShowInPos = true,
                ShowInCommander = false,
                ShowInCustomerQr = false,
                RequiresKds = false,
            },
            ct
        );

        Assert.NotNull(result);
        Assert.Equal("Bebidas frías", result.Name);
        Assert.True(result.ShowInPos);
        Assert.False(result.ShowInCommander);
        Assert.False(result.ShowInCustomerQr);
        Assert.False(result.RequiresKds);
        CategoryChanged change = JsonSerializer.Deserialize<CategoryChanged>(
            Assert.Single(db.OutboxMessages).Payload
        )!;
        Assert.Equal(category.Id, change.CategoryId);
        Assert.Equal(result.Version, change.Version);
    }
}
