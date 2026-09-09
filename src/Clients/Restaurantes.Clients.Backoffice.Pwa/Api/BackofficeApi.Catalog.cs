using System.Net.Http.Json;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<List<CategoryResponse>> CategoriesAsync()
    {
        return GetAsync<List<CategoryResponse>>("/api/catalog/categories");
    }

    public Task<List<ProductResponse>> ProductsAsync()
    {
        return GetAsync<List<ProductResponse>>("/api/catalog/products");
    }

    public Task<List<MenuItemResponse>> MenuAsync(Guid restaurantId)
    {
        return GetAsync<List<MenuItemResponse>>(
            $"/api/catalog/restaurants/{restaurantId}/menu/configuration"
        );
    }

    public Task<List<KitchenStationResponse>> StationsAsync(Guid restaurantId)
    {
        return GetAsync<List<KitchenStationResponse>>(
            $"/api/catalog/restaurants/{restaurantId}/stations"
        );
    }

    public async Task EnsureDefaultStationsAsync(Guid restaurantId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/catalog/restaurants/{restaurantId}/stations/defaults"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        await ReadAsync<List<KitchenStationResponse>>(response);
    }

    public async Task<KitchenStationResponse> CreateStationAsync(
        Guid restaurantId,
        KitchenStationDraft draft
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/catalog/restaurants/{restaurantId}/stations"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<KitchenStationResponse>(response);
    }

    public async Task<KitchenStationResponse> UpdateStationAsync(KitchenStationResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/restaurants/{item.RestaurantId}/stations/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.IsActive,
                item.IsPrimary,
                item.RequiresPrimaryDispatch,
                item.Priority,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<KitchenStationResponse>(response);
    }

    public async Task<MenuItemResponse> ConfigureAsync(
        Guid restaurantId,
        Guid productId,
        MenuEditor editor
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/restaurants/{restaurantId}/products/{productId}"
        );
        request.Content = JsonContent.Create(
            new
            {
                editor.Price,
                editor.IsAvailable,
                editor.PreparationStationCode,
                editor.PreparationStationName,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<MenuItemResponse>(response);
    }

    public async Task<CategoryResponse> CreateCategoryAsync(CategoryDraft draft)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/catalog/categories");
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CategoryResponse>(response);
    }

    public async Task<ProductResponse> CreateProductAsync(ProductDraft draft)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/catalog/products");
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ProductResponse>(response);
    }

    public async Task<CategoryResponse> UpdateCategoryAsync(CategoryResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/categories/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.DefaultStationCode,
                item.DefaultStationName,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CategoryResponse>(response);
    }

    public async Task<ProductResponse> UpdateProductAsync(ProductResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/products/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.CategoryId,
                item.BasePrice,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ProductResponse>(response);
    }
}
