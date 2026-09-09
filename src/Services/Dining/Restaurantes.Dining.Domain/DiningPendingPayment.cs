namespace Restaurantes.Dining.Domain;

public sealed class DiningPendingPayment
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = "Unpaid";
    public DateTime UpdatedAtUtc { get; set; }
}
