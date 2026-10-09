using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Restaurantes.Catalog.Infrastructure.Media;

public sealed record ProductImageVariant(string Name, byte[] Content, string ContentType);

public sealed record StoredProductImage(Stream Content, string ContentType);

public interface IProductImageStorage
{
    Task StoreAsync(
        Guid productId,
        Guid revision,
        string originalExtension,
        string originalContentType,
        byte[] original,
        IReadOnlyList<ProductImageVariant> variants,
        CancellationToken ct
    );

    Task<StoredProductImage?> OpenReadAsync(
        Guid productId,
        Guid revision,
        string variant,
        CancellationToken ct
    );

    Task DeleteAsync(Guid productId, Guid revision, CancellationToken ct);
}

public sealed class FileSystemProductImageStorage(
    IConfiguration configuration,
    IHostEnvironment environment
) : IProductImageStorage
{
    private string? Root =>
        configuration["CatalogMedia:Root"] is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
        : environment.IsDevelopment()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Restaurantes",
                "CatalogMedia"
            )
        : null;

    public async Task StoreAsync(
        Guid productId,
        Guid revision,
        string originalExtension,
        string originalContentType,
        byte[] original,
        IReadOnlyList<ProductImageVariant> variants,
        CancellationToken ct
    )
    {
        string root =
            Root
            ?? throw new InvalidOperationException(
                "Configura CatalogMedia:Root para usar almacenamiento de archivos."
            );
        string directory = DirectoryFor(root, productId, revision);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, $"original.{originalExtension}"),
            original,
            ct
        );
        foreach (ProductImageVariant variant in variants)
            await File.WriteAllBytesAsync(
                Path.Combine(directory, variant.Name),
                variant.Content,
                ct
            );
    }

    public Task<StoredProductImage?> OpenReadAsync(
        Guid productId,
        Guid revision,
        string variant,
        CancellationToken ct
    )
    {
        string? root = Root;
        if (root is null)
            return Task.FromResult<StoredProductImage?>(null);
        string path = Path.Combine(DirectoryFor(root, productId, revision), variant);
        if (!File.Exists(path))
            return Task.FromResult<StoredProductImage?>(null);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<StoredProductImage?>(new StoredProductImage(stream, "image/webp"));
    }

    public Task DeleteAsync(Guid productId, Guid revision, CancellationToken ct)
    {
        string? root = Root;
        if (root is null)
            return Task.CompletedTask;
        string directory = DirectoryFor(root, productId, revision);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    private static string DirectoryFor(string root, Guid productId, Guid revision) =>
        Path.Combine(root, productId.ToString("N"), revision.ToString("N"));
}

public sealed class AzureBlobProductImageStorage(IConfiguration configuration)
    : IProductImageStorage
{
    private BlobContainerClient Container =>
        new(
            configuration["CatalogMedia:Blob:ConnectionString"]
                ?? throw new InvalidOperationException("Falta CatalogMedia:Blob:ConnectionString."),
            configuration["CatalogMedia:Blob:Container"] ?? "catalog-media"
        );

    public async Task StoreAsync(
        Guid productId,
        Guid revision,
        string originalExtension,
        string originalContentType,
        byte[] original,
        IReadOnlyList<ProductImageVariant> variants,
        CancellationToken ct
    )
    {
        BlobContainerClient container = Container;
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        await UploadAsync(
            container,
            PathFor(productId, revision, $"original.{originalExtension}"),
            original,
            originalContentType,
            ct
        );
        foreach (ProductImageVariant variant in variants)
            await UploadAsync(
                container,
                PathFor(productId, revision, variant.Name),
                variant.Content,
                variant.ContentType,
                ct
            );
    }

    public async Task<StoredProductImage?> OpenReadAsync(
        Guid productId,
        Guid revision,
        string variant,
        CancellationToken ct
    )
    {
        BlobClient blob = Container.GetBlobClient(PathFor(productId, revision, variant));
        if (!await blob.ExistsAsync(ct))
            return null;
        Response<BlobDownloadStreamingResult> response = await blob.DownloadStreamingAsync(
            cancellationToken: ct
        );
        return new StoredProductImage(response.Value.Content, "image/webp");
    }

    public async Task DeleteAsync(Guid productId, Guid revision, CancellationToken ct)
    {
        BlobContainerClient container = Container;
        string prefix = $"{productId:N}/{revision:N}/";
        await foreach (
            BlobItem item in container.GetBlobsAsync(prefix: prefix, cancellationToken: ct)
        )
            await container.DeleteBlobIfExistsAsync(item.Name, cancellationToken: ct);
    }

    private static async Task UploadAsync(
        BlobContainerClient container,
        string name,
        byte[] content,
        string contentType,
        CancellationToken ct
    )
    {
        BlobHttpHeaders headers = new()
        {
            ContentType = contentType,
            CacheControl = "public, max-age=31536000, immutable",
        };
        await container
            .GetBlobClient(name)
            .UploadAsync(
                BinaryData.FromBytes(content),
                new BlobUploadOptions { HttpHeaders = headers },
                ct
            );
    }

    private static string PathFor(Guid productId, Guid revision, string name) =>
        $"{productId:N}/{revision:N}/{name}";
}

public static class CatalogMediaServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogMedia(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        string provider = configuration["CatalogMedia:Provider"] ?? "FileSystem";
        if (provider.Equals("Blob", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IProductImageStorage, AzureBlobProductImageStorage>();
        else
            services.AddSingleton<IProductImageStorage, FileSystemProductImageStorage>();
        return services;
    }
}
