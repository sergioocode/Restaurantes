using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Application;

public sealed class CatalogCommandService(ICatalogWriteStore store, TimeProvider timeProvider)
{
    private static readonly (
        string Code,
        string Name,
        bool IsPrimary,
        bool RequiresPrimaryDispatch,
        int Priority
    )[] DefaultStations =
    [
        ("CHEF", "Vista completa", true, false, 0),
        ("ENTRANTES", "Entrantes", false, true, 1),
        ("BEBIDAS", "Bebidas", false, false, 1),
        ("PASTAS", "Pastas", false, true, 2),
        ("CARNES", "Carnes", false, true, 2),
        ("POSTRES", "Postres", false, false, 3),
    ];

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

    public async Task<MenuItemResponse?> ConfigureMenuItemAsync(
        Guid restaurantId,
        Guid productId,
        ConfigureMenuItemRequest request,
        CancellationToken ct
    )
    {
        Product? product = await store.FindProductAsync(productId, ct);
        if (product is null || !product.IsActive)
        {
            return null;
        }

        Category? category = await store.FindCategoryAsync(product.CategoryId, ct);
        if (category is null || !category.IsActive)
        {
            return null;
        }

        string stationCode = string.IsNullOrWhiteSpace(request.PreparationStationCode)
            ? category.DefaultStationCode
            : request.PreparationStationCode;
        string stationName = string.IsNullOrWhiteSpace(request.PreparationStationName)
            ? category.DefaultStationName
            : request.PreparationStationName;
        RestaurantKitchenStation? station = await store.FindStationByCodeAsync(
            restaurantId,
            stationCode.Trim().ToUpperInvariant(),
            ct
        );
        if (station is null || !station.IsActive)
        {
            return null;
        }
        stationCode = station.Code;
        stationName = station.Name;
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RestaurantMenuItem? item = await store.FindMenuItemAsync(restaurantId, productId, ct);
        if (item is null)
        {
            item = RestaurantMenuItem.Create(
                restaurantId,
                productId,
                request.Price,
                request.IsAvailable,
                stationCode,
                stationName,
                now
            );
        }
        else
        {
            item.Configure(request.Price, request.IsAvailable, stationCode, stationName, now);
        }

        CatalogItemChanged message = new(
            restaurantId,
            product.Id,
            product.Sku,
            product.Name,
            category.Id,
            category.Code,
            category.Name,
            item.Price,
            item.IsAvailable,
            item.PreparationStationCode,
            item.PreparationStationName,
            item.Version,
            now
        );
        await store.SaveWithEventAsync(item, message, ct);
        return new(
            restaurantId,
            product.Id,
            product.Sku,
            product.Name,
            category.Id,
            category.Code,
            category.Name,
            item.Price,
            item.IsAvailable,
            item.PreparationStationCode,
            item.PreparationStationName,
            item.Version,
            now
        );
    }

    public async Task<KitchenStationResponse?> CreateStationAsync(
        Guid restaurantId,
        CreateKitchenStationRequest request,
        CancellationToken ct
    )
    {
        string code = request.Code.Trim().ToUpperInvariant();
        if (await store.FindStationByCodeAsync(restaurantId, code, ct) is not null)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RestaurantKitchenStation item = RestaurantKitchenStation.Create(
            restaurantId,
            code,
            request.Name,
            request.IsPrimary,
            request.RequiresPrimaryDispatch,
            request.Priority,
            now
        );
        if (item.IsPrimary && await store.HasOtherActivePrimaryStationAsync(restaurantId, null, ct))
        {
            throw new InvalidOperationException(
                "Only one active primary KDS is allowed per restaurant."
            );
        }
        await SaveStationAsync(item, now, ct);
        return ToResponse(item);
    }

    public async Task<KitchenStationResponse?> UpdateStationAsync(
        Guid restaurantId,
        Guid stationId,
        UpdateKitchenStationRequest request,
        CancellationToken ct
    )
    {
        RestaurantKitchenStation? item = await store.FindStationAsync(restaurantId, stationId, ct);
        if (item is null)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (
            request.IsActive
            && request.IsPrimary
            && await store.HasOtherActivePrimaryStationAsync(restaurantId, stationId, ct)
        )
        {
            throw new InvalidOperationException(
                "Only one active primary KDS is allowed per restaurant."
            );
        }
        item.Update(
            request.Name,
            request.IsActive,
            request.IsPrimary,
            request.RequiresPrimaryDispatch,
            request.Priority,
            now
        );
        await SaveStationAsync(item, now, ct);
        return ToResponse(item);
    }

    public async Task<IReadOnlyList<KitchenStationResponse>> EnsureDefaultStationsAsync(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        List<KitchenStationResponse> result = [];
        foreach (
            (
                string code,
                string name,
                bool isPrimary,
                bool requiresPrimaryDispatch,
                int priority
            ) in DefaultStations
        )
        {
            RestaurantKitchenStation? item = await store.FindStationByCodeAsync(
                restaurantId,
                code,
                ct
            );
            if (item is null)
            {
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                item = RestaurantKitchenStation.Create(
                    restaurantId,
                    code,
                    name,
                    isPrimary,
                    requiresPrimaryDispatch,
                    priority,
                    now
                );
                await SaveStationAsync(item, now, ct);
            }
            result.Add(ToResponse(item));
        }
        return result;
    }

    private async Task SaveStationAsync(
        RestaurantKitchenStation item,
        DateTime now,
        CancellationToken ct
    )
    {
        KitchenStationChanged message = new(
            item.Id,
            item.RestaurantId,
            item.Code,
            item.Name,
            item.IsPrimary,
            item.RequiresPrimaryDispatch,
            item.Priority,
            item.IsActive,
            item.Version,
            now
        );
        await store.SaveWithEventAsync(item, message, ct);
    }

    private static KitchenStationResponse ToResponse(RestaurantKitchenStation item)
    {
        return new(
            item.Id,
            item.RestaurantId,
            item.Code,
            item.Name,
            item.IsPrimary,
            item.RequiresPrimaryDispatch,
            item.Priority,
            item.IsActive,
            item.Version,
            item.UpdatedAtUtc
        );
    }
}
