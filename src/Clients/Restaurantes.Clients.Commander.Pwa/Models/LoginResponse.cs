namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    LoginUser User,
    List<RestaurantAccess> Restaurants
);
