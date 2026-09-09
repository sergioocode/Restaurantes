using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Payments.Contracts.Requests;

public sealed class CaptureCustomerQrPaymentRequest
{
    public Guid IdempotencyKey { get; init; }
    public Guid DiningSessionId { get; init; }

    [StringLength(160)]
    public string ExternalReference { get; init; } = string.Empty;
}
