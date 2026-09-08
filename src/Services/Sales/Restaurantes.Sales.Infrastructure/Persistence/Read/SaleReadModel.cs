namespace Restaurantes.Sales.Infrastructure.Persistence.Read;

public sealed class SaleReadModel
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Status { get; set; } = "Completed";
    public int OrderCount { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string OrderIdsJson { get; set; } = "[]";
    public string LinesJson { get; set; } = "[]";
}
