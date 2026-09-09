using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Orders.Contracts.Requests;

public sealed class CreateOrderLineRequest
{
    public Guid ProductId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }

    [StringLength(500)]
    public string Notes { get; init; } = "";
}
