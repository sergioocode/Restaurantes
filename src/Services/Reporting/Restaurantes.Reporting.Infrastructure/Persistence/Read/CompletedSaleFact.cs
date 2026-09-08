namespace Restaurantes.Reporting.Infrastructure.Persistence.Read;

public sealed class CompletedSaleFact
{
    public Guid SaleId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string OrderIdsJson { get; set; } = "[]";
    public string LinesJson { get; set; } = "[]";
}
