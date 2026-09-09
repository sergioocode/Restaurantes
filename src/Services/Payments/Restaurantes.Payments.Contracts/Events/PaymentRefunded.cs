namespace Restaurantes.Payments.Contracts.Events;

public sealed record PaymentRefunded(
    Guid PaymentId,
    Guid OrderId,
    Guid RestaurantId,
    decimal Amount,
    string Method,
    string ServiceMode,
    string Source,
    string Reason,
    DateTime RefundedAtUtc
);
