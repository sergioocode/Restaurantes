using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Application;

public sealed partial class CatalogCommandService
{
    public async Task<CategoryResponse?> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken ct
    )
    {
        string code = request.Code.Trim().ToUpperInvariant();
        if (await store.CategoryCodeExistsAsync(code, ct))
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Category item = Category.Create(
            code,
            request.Name,
            request.DefaultStationCode,
            request.DefaultStationName,
            now
        );
        CategoryChanged message = new(
            item.Id,
            item.Code,
            item.Name,
            item.DefaultStationCode,
            item.DefaultStationName,
            item.IsActive,
            item.Version,
            now
        );
        await store.SaveWithEventAsync(item, message, ct);
        return new(
            item.Id,
            item.Code,
            item.Name,
            item.DefaultStationCode,
            item.DefaultStationName,
            item.IsActive,
            item.Version,
            now
        );
    }

    public async Task<CategoryResponse?> UpdateCategoryAsync(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken ct
    )
    {
        Category? item = await store.FindCategoryAsync(id, ct);
        if (item is null)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        item.Update(
            request.Name,
            request.DefaultStationCode,
            request.DefaultStationName,
            request.IsActive,
            now
        );
        CategoryChanged message = new(
            item.Id,
            item.Code,
            item.Name,
            item.DefaultStationCode,
            item.DefaultStationName,
            item.IsActive,
            item.Version,
            now
        );
        await store.SaveWithEventAsync(item, message, ct);
        return new(
            item.Id,
            item.Code,
            item.Name,
            item.DefaultStationCode,
            item.DefaultStationName,
            item.IsActive,
            item.Version,
            now
        );
    }
}
