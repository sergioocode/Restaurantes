namespace Restaurantes.Clients.CustomerQr.Pwa.Models;

public sealed record QrDiningSessionResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    bool RequestGuestCount = false,
    int? GuestCount = null
);
