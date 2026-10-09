namespace Restaurantes.Payments.Contracts.Responses;

public sealed record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    Guid RestaurantId,
    decimal Amount,
    string Method,
    string Status,
    DateTime? CapturedAtUtc,
    DateTime? RefundedAtUtc,
    decimal TipAmount = 0,
    decimal AmountPaid = 0,
    decimal Outstanding = 0,
    decimal TipAmountPaid = 0
);
