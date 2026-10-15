namespace Restaurantes.Dining.Domain;

public sealed class DiningSessionOrder
{
    public Guid OrderId { get; set; }
    public Guid DiningSessionId { get; set; }
    public string Source { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid";
    public string OrderStatus { get; set; } = "Draft";
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal TipPaid { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
