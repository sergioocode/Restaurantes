namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class ProductDraft
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
}
