using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Payments.Contracts;

public sealed class CapturePaymentRequest
{
    public Guid IdempotencyKey { get; init; }

    [Range(1, 100)]
    public int TransactionOrderCount { get; init; } = 1;

    [Required, RegularExpression("^(Online|Card|Cash)$")]
    public string Method { get; init; } = "Card";

    [StringLength(160)]
    public string ExternalReference { get; init; } = string.Empty;
}

public sealed class CaptureCustomerQrPaymentRequest
{
    public Guid IdempotencyKey { get; init; }
    public Guid DiningSessionId { get; init; }

    [StringLength(160)]
    public string ExternalReference { get; init; } = string.Empty;
}

public sealed class RefundPaymentRequest
{
    [Required, StringLength(300)]
    public string Reason { get; init; } = string.Empty;
}

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
