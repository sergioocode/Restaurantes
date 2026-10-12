using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class UpdateProductRequest
{
    [Required, StringLength(160)]
    public string Name { get; init; } = "";
    public Guid CategoryId { get; init; }
}
