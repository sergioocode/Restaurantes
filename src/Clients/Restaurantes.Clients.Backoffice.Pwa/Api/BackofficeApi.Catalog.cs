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

    public Task<List<KitchenStationResponse>> StationsAsync(Guid restaurantId)
    {
        return GetAsync<List<KitchenStationResponse>>(
            $"/api/catalog/restaurants/{restaurantId}/stations"
        );
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

    public Task<KitchenStationResponse> SetStationNameAsync(
        KitchenStationResponse item,
        string name
    ) => SetStationFieldAsync(item, "name", name);

    public Task<KitchenStationResponse> SetStationPriorityAsync(
        KitchenStationResponse item,
        int priority
    ) => SetStationFieldAsync(item, "priority", priority);

    public Task<KitchenStationResponse> SetStationFlagAsync(
        KitchenStationResponse item,
        string field,
        bool selected
    ) =>
        field switch
        {
            nameof(KitchenStationResponse.IsActive) => SetStationFieldAsync(
                item,
                "active",
                selected
            ),
            nameof(KitchenStationResponse.IsPrimary) => SetStationFieldAsync(
                item,
                "primary",
                selected
            ),
            nameof(KitchenStationResponse.RequiresPrimaryDispatch) => SetStationFieldAsync(
                item,
                "requires-primary-dispatch",
                selected
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

    private async Task<KitchenStationResponse> SetStationFieldAsync<T>(
        KitchenStationResponse item,
        string field,
        T value
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/restaurants/{item.RestaurantId}/stations/{item.Id}/{field}"
        );
        request.Content = JsonContent.Create(value);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<KitchenStationResponse>(response);
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
                item.RequiresKds,
                item.IsActive,
                item.ShowInPos,
                item.ShowInCommander,
                item.ShowInCustomerQr,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CategoryResponse>(response);
    }

    public async Task<ProductResponse> SetProductCategoryAsync(Guid id, Guid categoryId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/products/{id}/category"
        );
        request.Content = JsonContent.Create(categoryId);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ProductResponse>(response);
    }

    public async Task<ProductResponse> SetProductNameAsync(Guid id, string name)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/products/{id}/name"
        );
        request.Content = JsonContent.Create(name);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ProductResponse>(response);
    }

    public async Task<ProductResponse> SetProductActiveAsync(Guid id, bool active)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/catalog/products/{id}/active"
        );
        request.Content = JsonContent.Create(active);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ProductResponse>(response);
    }
}
