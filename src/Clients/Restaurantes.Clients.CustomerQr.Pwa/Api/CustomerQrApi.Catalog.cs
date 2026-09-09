using Restaurantes.Clients.CustomerQr.Pwa.Models;

namespace Restaurantes.Clients.CustomerQr.Pwa.Api;

public sealed partial class CustomerQrApi
{
    public async Task<List<MenuItemResponse>> MenuAsync(Guid restaurantId)
    {
        using HttpResponseMessage response = await http.GetAsync(
            $"/api/catalog/restaurants/{restaurantId}/menu"
        );
        return await ReadAsync<List<MenuItemResponse>>(response);
    }

    public async Task<RestaurantResponse> RestaurantAsync(Guid restaurantId)
    {
        using HttpResponseMessage response = await http.GetAsync(
            $"/api/restaurant-operations/restaurants/{restaurantId}"
        );
        return await ReadAsync<RestaurantResponse>(response);
    }
}
