using System.Net.Http.Json;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<List<RestaurantResponse>> RestaurantsAsync()
    {
        return GetAsync<List<RestaurantResponse>>("/api/restaurant-operations/restaurants");
    }

    public async Task<RestaurantResponse> CreateRestaurantAsync(RestaurantDraft draft)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            "/api/restaurant-operations/restaurants"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<RestaurantResponse>(response);
    }

    public async Task<RestaurantResponse> UpdateRestaurantAsync(RestaurantResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/restaurant-operations/restaurants/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Name,
                item.Address,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<RestaurantResponse>(response);
    }
}
