namespace Restaurantes.Clients.CustomerQr.Pwa.Models;

public sealed record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    Guid RestaurantId,
    decimal Amount,
    string Method,
    string Status,
    DateTime? CapturedAtUtc,
    DateTime? RefundedAtUtc
);
