namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record DiningSessionResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Source,
    string Status,
    List<DiningSessionOrderResponse> Orders,
    bool RequestGuestCount = false,
    int? GuestCount = null
);
