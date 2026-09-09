using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Payments.Contracts.Requests;

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
