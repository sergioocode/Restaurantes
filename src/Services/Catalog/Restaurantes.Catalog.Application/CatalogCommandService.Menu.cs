using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Application;

public sealed partial class CatalogCommandService
{
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
}
