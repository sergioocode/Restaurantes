namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record RestaurantResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    bool IsActive
);
