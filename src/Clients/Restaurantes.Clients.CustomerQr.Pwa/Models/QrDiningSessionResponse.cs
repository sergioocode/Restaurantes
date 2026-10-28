namespace Restaurantes.Clients.CustomerQr.Pwa.Models;

public sealed record QrDiningSessionResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    DateTime? QrReservationExpiresAtUtc = null
);
