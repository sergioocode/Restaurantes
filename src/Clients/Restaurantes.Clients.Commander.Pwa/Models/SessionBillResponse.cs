namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record SessionBillResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    decimal Total,
    decimal Outstanding,
    List<SessionBillOrderResponse> Orders,
    bool CanCheckout = false,
    bool CanRelease = false,
    int? GuestCount = null,
    string PaymentMethod = "",
    DateTime? PaidAtUtc = null,
    decimal TipTotal = 0,
    decimal DiscountPercentage = 0,
    decimal DiscountAmount = 0
);
