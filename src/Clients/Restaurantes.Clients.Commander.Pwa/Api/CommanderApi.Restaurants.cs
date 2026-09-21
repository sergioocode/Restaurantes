using Restaurantes.Clients.Commander.Pwa.Models;

namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi
{
    public Task<List<RestaurantResponse>> RestaurantsAsync()
    {
        return GetAsync<List<RestaurantResponse>>(
            "/api/restaurant-operations/restaurants/accessible/commander"
        );
    }
}
