namespace Restaurantes.Dining.Domain;

public sealed class DiningSessionOrder
{
    public Guid OrderId { get; set; }
    public Guid DiningSessionId { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid";
    public string OrderStatus { get; set; } = "Draft";
    public decimal Amount { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
