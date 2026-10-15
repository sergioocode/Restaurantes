namespace Restaurantes.Dining.Domain;

public sealed class DiningPendingPayment
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = "Unpaid";
    public decimal AmountPaid { get; set; }
    public decimal TipPaid { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
