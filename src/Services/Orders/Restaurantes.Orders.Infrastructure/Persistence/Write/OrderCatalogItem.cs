namespace Restaurantes.Orders.Infrastructure.Persistence.Write;

public sealed class OrderCatalogItem
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
