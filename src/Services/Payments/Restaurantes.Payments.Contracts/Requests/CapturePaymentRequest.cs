using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Payments.Contracts.Requests;

public sealed class CapturePaymentRequest
{
    public Guid IdempotencyKey { get; init; }

    [Range(0, 1_000_000)]
    public decimal? Amount { get; init; }

    [Range(0, 1_000_000)]
    public decimal TipAmount { get; init; }

    [Range(0, 1_000_000)]
    public decimal? TenderedAmount { get; init; }

    [Range(1, 100)]
    public int TransactionOrderCount { get; init; } = 1;

    public Guid? SaleGroupId { get; init; }

    public string? DiscountName { get; init; }

    [Range(0, 99.99)]
    public decimal DiscountPercentage { get; init; }

    [Range(0, 1_000_000)]
    public decimal DiscountAmount { get; init; }

    [Required, RegularExpression("^(Online|Card|Cash|Transfer)$")]
    public string Method { get; init; } = "Card";

    [StringLength(160)]
    public string ExternalReference { get; init; } = string.Empty;
}
