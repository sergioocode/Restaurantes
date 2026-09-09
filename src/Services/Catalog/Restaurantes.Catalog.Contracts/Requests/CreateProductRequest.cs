using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Catalog.Contracts.Requests;

public sealed class CreateProductRequest
{
    [Required, StringLength(60), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Sku { get; init; } = "";

    [Required, StringLength(160)]
    public string Name { get; init; } = "";
    public Guid CategoryId { get; init; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal BasePrice { get; init; }
}
