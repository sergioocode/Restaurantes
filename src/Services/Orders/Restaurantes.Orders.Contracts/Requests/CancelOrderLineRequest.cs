using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Orders.Contracts.Requests;

public sealed class CancelOrderLineRequest
{
    [Required, StringLength(300, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}
