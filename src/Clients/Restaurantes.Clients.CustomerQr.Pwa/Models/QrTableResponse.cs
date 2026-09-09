namespace Restaurantes.Clients.CustomerQr.Pwa.Models;

public sealed record QrTableResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Label,
    bool RequestGuestCount = false
);
