namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record SessionBillResponse(
    Guid Id,
    Guid RestaurantId,
    Guid? TableId,
    string Status,
    string PaymentMethod,
    DateTime? PaidAtUtc,
    decimal Total,
    decimal Outstanding,
    bool CanCheckout,
    bool CanRelease,
    List<SessionBillOrderResponse> Orders,
    bool RequestGuestCount = false,
    int? GuestCount = null,
    decimal TipTotal = 0,
    decimal DiscountPercentage = 0,
    decimal DiscountAmount = 0,
    string CustomerName = ""
);
