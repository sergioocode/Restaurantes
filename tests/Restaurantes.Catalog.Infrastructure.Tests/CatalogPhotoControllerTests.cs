using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Api.Controllers;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Media;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogPhotoControllerTests
{
    [Theory]
    [InlineData(8_000_001, 1, 413)]
    [InlineData(8_000_000, 7_000_001, 400)]
    [InlineData(0, 0, 400)]
    public async Task Upload_rejects_oversized_requests_and_invalid_file_sizes(
        long requestLength,
        long fileLength,
        int expectedStatus
    )
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Product product = Product.Create("PHOTO", "Foto", Guid.NewGuid(), DateTime.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        RecordingStorage storage = new();
        CatalogPhotoController controller = CreateController(db, storage);
        controller.Request.ContentLength = requestLength;
        using MemoryStream stream = new();
        FormFile file = new(stream, 0, fileLength, "original", "photo.png");

        IActionResult result = await controller.Upload(
            product.Id,
            file,
            TestContext.Current.CancellationToken
        );

        int? actualStatus = result switch
        {
            StatusCodeResult status => status.StatusCode,
            ObjectResult status => status.StatusCode,
            _ => null,
        };
        Assert.Equal(expectedStatus, actualStatus);
        Assert.Equal(0, storage.Writes);
        Assert.Empty(db.ProductPhotos);
    }

    [Fact]
    public async Task Upload_persists_the_revision_and_variants_for_a_valid_photo()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        Product product = Product.Create("PHOTO", "Foto", Guid.NewGuid(), DateTime.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        RecordingStorage storage = new();
        CatalogPhotoController controller = CreateController(db, storage);
        using SkiaSharp.SKBitmap bitmap = new(2, 2);
        using SkiaSharp.SKData png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(png.ToArray());
        FormFile file = new(stream, 0, stream.Length, "original", "photo.png");
        controller.Request.ContentLength = stream.Length + 200;

        IActionResult result = await controller.Upload(
            product.Id,
            file,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<OkObjectResult>(result);
        ProductPhoto saved = Assert.Single(db.ProductPhotos);
        Assert.Equal(product.Id, saved.ProductId);
        Assert.Equal(storage.Revision, saved.Revision);
        Assert.Equal("image/png", saved.OriginalContentType);
        Assert.Equal(1, storage.Writes);
        Assert.Equal(["thumbnail.webp", "card.webp", "detail.webp"], storage.Names);
    }

    [Fact]
    public async Task Upload_requires_catalog_management_permission()
    {
        await using CatalogWriteDbContext db = CreateDatabase();
        RecordingStorage storage = new();
        CatalogPhotoController controller = CreateController(db, storage);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        using MemoryStream stream = new();

        IActionResult result = await controller.Upload(
            Guid.NewGuid(),
            new FormFile(stream, 0, 1, "original", "photo.png"),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(0, storage.Writes);
    }

    private static CatalogWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static CatalogPhotoController CreateController(
        CatalogWriteDbContext db,
        RecordingStorage storage
    ) =>
        new(db, storage)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test")
                    ),
                },
            },
        };

    private sealed class RecordingStorage : IProductImageStorage
    {
        public int Writes { get; private set; }
        public Guid Revision { get; private set; }
        public string[] Names { get; private set; } = [];

        public Task StoreAsync(
            Guid productId,
            Guid revision,
            IReadOnlyList<ProductImageVariant> variants,
            CancellationToken ct
        )
        {
            Writes++;
            Revision = revision;
            Names = variants.Select(variant => variant.Name).ToArray();
            return Task.CompletedTask;
        }

        public Task<StoredProductImage?> OpenReadAsync(
            Guid productId,
            Guid revision,
            string variant,
            CancellationToken ct
        ) => Task.FromResult<StoredProductImage?>(null);

        public Task DeleteAsync(Guid productId, Guid revision, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
