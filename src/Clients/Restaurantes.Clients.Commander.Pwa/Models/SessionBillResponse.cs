namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record SessionBillResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    decimal Total,
    decimal Outstanding,
    List<SessionBillOrderResponse> Orders
);
