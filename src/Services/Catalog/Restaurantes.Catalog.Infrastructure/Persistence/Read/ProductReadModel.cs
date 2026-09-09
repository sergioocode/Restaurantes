namespace Restaurantes.Catalog.Infrastructure.Persistence.Read;

public sealed class ProductReadModel
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
