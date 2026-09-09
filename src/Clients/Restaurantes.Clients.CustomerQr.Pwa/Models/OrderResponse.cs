namespace Restaurantes.Clients.CustomerQr.Pwa.Models;

public sealed record OrderResponse(
    Guid Id,
    Guid RestaurantId,
    string Status,
    decimal Total,
    int Version,
    DateTime CreatedAtUtc
);
