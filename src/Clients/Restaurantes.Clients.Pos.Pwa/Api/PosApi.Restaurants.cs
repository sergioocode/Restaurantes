using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public Task<List<RestaurantResponse>> RestaurantsAsync()
    {
        return GetAsync<List<RestaurantResponse>>(
            "/api/restaurant-operations/restaurants/accessible/pos"
        );
    }
}
