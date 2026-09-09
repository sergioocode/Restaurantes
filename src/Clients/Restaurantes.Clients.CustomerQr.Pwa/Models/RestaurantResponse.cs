namespace Restaurantes.Clients.CustomerQr.Pwa.Models;

public sealed record RestaurantResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);
