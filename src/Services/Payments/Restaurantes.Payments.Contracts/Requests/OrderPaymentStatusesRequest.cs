using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Payments.Contracts.Requests;

public sealed class OrderPaymentStatusesRequest
{
    [Required, MinLength(1), MaxLength(500)]
    public Guid[] OrderIds { get; init; } = [];
}
