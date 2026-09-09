namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record SessionBillResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    string PaymentMethod,
    DateTime? PaidAtUtc,
    decimal Total,
    decimal Outstanding,
    bool CanCheckout,
    List<SessionBillOrderResponse> Orders
);
