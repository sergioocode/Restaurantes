using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public Task<List<MenuItemResponse>> MenuAsync(Guid restaurantId)
    {
        return GetAsync<List<MenuItemResponse>>($"/api/catalog/restaurants/{restaurantId}/menu");
    }
}
