namespace Restaurantes.Payments.Contracts.Events;

public sealed record PaymentCaptured(
    Guid PaymentId,
    Guid OrderId,
    Guid RestaurantId,
    decimal Amount,
    string Method,
    string ServiceMode,
    string Source,
    string ExternalReference,
    DateTime CapturedAtUtc,
    Guid? PaymentTransactionId = null,
    int TransactionOrderCount = 1
);
