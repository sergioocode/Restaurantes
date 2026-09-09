using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Application;

public sealed partial class CatalogCommandService
{
    public async Task<ProductResponse?> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken ct
    )
    {
        string sku = request.Sku.Trim().ToUpperInvariant();
        if (
            await store.ProductSkuExistsAsync(sku, ct)
            || await store.FindCategoryAsync(request.CategoryId, ct) is null
        )
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Product item = Product.Create(
            sku,
            request.Name,
            request.CategoryId,
            request.BasePrice,
            now
        );
        ProductChanged message = new(
            item.Id,
            item.Sku,
            item.Name,
            item.CategoryId,
            item.BasePrice,
            item.IsActive,
            item.Version,
            now
        );
        await store.SaveWithEventAsync(item, message, ct);
        return new(
            item.Id,
            item.Sku,
            item.Name,
            item.CategoryId,
            item.BasePrice,
            item.IsActive,
            item.Version,
            now
        );
    }

    public async Task<ProductResponse?> UpdateProductAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken ct
    )
    {
        Product? item = await store.FindProductAsync(id, ct);
        Category? category = await store.FindCategoryAsync(request.CategoryId, ct);
        if (item is null || category is null)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        item.Update(request.Name, request.CategoryId, request.BasePrice, request.IsActive, now);
        ProductChanged message = new(
            item.Id,
            item.Sku,
            item.Name,
            item.CategoryId,
            item.BasePrice,
            item.IsActive,
            item.Version,
            now
        );
        await store.SaveWithEventAsync(item, message, ct);
        return new(
            item.Id,
            item.Sku,
            item.Name,
            item.CategoryId,
            item.BasePrice,
            item.IsActive,
            item.Version,
            now
        );
    }
}
