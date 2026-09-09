namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record DiningSessionResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    bool RequestGuestCount = false,
    int? GuestCount = null
);
