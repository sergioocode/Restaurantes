namespace Restaurantes.Orders.Infrastructure.Persistence.Write;

public sealed class OrderPaymentProjection
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Unpaid";
    public DateTime UpdatedAtUtc { get; set; }
}
