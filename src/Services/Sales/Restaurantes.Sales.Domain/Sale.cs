namespace Restaurantes.Sales.Domain;

public sealed class Sale
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public int ExpectedOrderCount { get; set; } = 1;
    public string Status { get; set; } = "Pending";
    public decimal Total { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public List<SaleOrder> Orders { get; set; } = [];

    public bool TryComplete(DateTime now)
    {
        if (
            Status == "Completed"
            || Orders.Count < ExpectedOrderCount
            || Orders.Any(x => !x.IsPaid)
        )
        {
            return false;
        }

        Status = "Completed";
        Total = Orders.Sum(x => x.Amount);
        CompletedAtUtc = now;
        return true;
    }
}

public sealed class SaleOrder
{
    public Guid SaleId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }
    public bool IsDelivered { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string LinesJson { get; set; } = "[]";
}
