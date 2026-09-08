namespace Restaurantes.Sales.Infrastructure.Persistence.Write;

public sealed class DeliveredOrderMarker
{
    public Guid OrderId { get; set; }
    public Guid RestaurantId { get; set; }
    public DateTime DeliveredAtUtc { get; set; }
    public string LinesJson { get; set; } = "[]";
}
