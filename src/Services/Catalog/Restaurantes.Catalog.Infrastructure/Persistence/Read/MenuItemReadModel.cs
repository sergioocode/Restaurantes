namespace Restaurantes.Catalog.Infrastructure.Persistence.Read;

public sealed class MenuItemReadModel
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string ProductName { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string CategoryCode { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public string PreparationStationCode { get; set; } = "";
    public string PreparationStationName { get; set; } = "";
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
