namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    LoginUser User,
    List<RestaurantAccess> Restaurants,
    List<string>? GlobalRoles = null
);
