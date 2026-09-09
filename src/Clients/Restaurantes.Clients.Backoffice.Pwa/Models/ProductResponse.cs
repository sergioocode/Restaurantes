namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class ProductResponse
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
