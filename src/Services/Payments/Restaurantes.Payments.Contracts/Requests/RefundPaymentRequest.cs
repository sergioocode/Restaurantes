using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Payments.Contracts.Requests;

public sealed class RefundPaymentRequest
{
    [Required, StringLength(300)]
    public string Reason { get; init; } = string.Empty;
}
