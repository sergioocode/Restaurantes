using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Restaurantes.Catalog.Infrastructure.Media;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class ProductImageStorageTests
{
    [Fact]
    public async Task File_storage_writes_reads_and_deletes_the_product_revision()
    {
        string root = Path.Combine(Path.GetTempPath(), $"catalog-media-{Guid.NewGuid():N}");
        Guid productId = Guid.NewGuid();
        Guid revision = Guid.NewGuid();
        byte[] card = [1, 2, 3];
        byte[] detail = [4, 5, 6, 7];
        FileSystemProductImageStorage storage = new(Configuration(root), Environment());

        await storage.StoreAsync(
            productId,
            revision,
            [
                new ProductImageVariant("card.webp", card, "image/webp"),
                new ProductImageVariant("detail.webp", detail, "image/webp"),
            ],
            TestContext.Current.CancellationToken
        );

        StoredProductImage stored = (
            await storage.OpenReadAsync(
                productId,
                revision,
                "card.webp",
                TestContext.Current.CancellationToken
            )
        )!;
        using (stored.Content)
        using (MemoryStream content = new())
        {
            await stored.Content.CopyToAsync(content, TestContext.Current.CancellationToken);
            Assert.Equal(card, content.ToArray());
            Assert.Equal("image/webp", stored.ContentType);
        }
        Assert.Null(
            await storage.OpenReadAsync(
                productId,
                revision,
                "missing.webp",
                TestContext.Current.CancellationToken
            )
        );

        await storage.DeleteAsync(productId, revision, TestContext.Current.CancellationToken);

        Assert.Null(
            await storage.OpenReadAsync(
                productId,
                revision,
                "card.webp",
                TestContext.Current.CancellationToken
            )
        );
        Assert.False(
            Directory.Exists(Path.Combine(root, productId.ToString("N"), revision.ToString("N")))
        );
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task File_storage_requires_a_root_outside_development_but_read_and_delete_are_no_ops()
    {
        FileSystemProductImageStorage storage = new(Configuration(null), Environment());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.StoreAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                [],
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("CatalogMedia:Root", error.Message);
        Assert.Null(
            await storage.OpenReadAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "card.webp",
                TestContext.Current.CancellationToken
            )
        );
        await storage.DeleteAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TestContext.Current.CancellationToken
        );
    }

    [Theory]
    [InlineData("FileSystem", typeof(FileSystemProductImageStorage))]
    [InlineData("Blob", typeof(AzureBlobProductImageStorage))]
    [InlineData("blob", typeof(AzureBlobProductImageStorage))]
    public void Service_registration_selects_the_configured_provider(
        string provider,
        Type expectedType
    )
    {
        ServiceCollection services = new();
        ConfigurationManager configuration = new();
        configuration["CatalogMedia:Provider"] = provider;
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(Environment());

        services.AddCatalogMedia(configuration, Environment());

        using ServiceProvider providerServices = services.BuildServiceProvider();
        Assert.IsType(expectedType, providerServices.GetRequiredService<IProductImageStorage>());
    }

    [Fact]
    public async Task Blob_storage_reports_missing_connection_string_before_accessing_the_service()
    {
        AzureBlobProductImageStorage storage = new(Configuration(null));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.StoreAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                [],
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("CatalogMedia:Blob:ConnectionString", error.Message);
    }

    private static IConfiguration Configuration(string? root) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                root is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?> { ["CatalogMedia:Root"] = root }
            )
            .Build();

    private static TestHostEnvironment Environment() =>
        new TestHostEnvironment
        {
            EnvironmentName = Environments.Production,
            ApplicationName = nameof(ProductImageStorageTests),
            ContentRootPath = Path.GetTempPath(),
            ContentRootFileProvider = new NullFileProvider(),
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
