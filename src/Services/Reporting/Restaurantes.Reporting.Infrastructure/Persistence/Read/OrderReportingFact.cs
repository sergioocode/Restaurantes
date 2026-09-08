namespace Restaurantes.Reporting.Infrastructure.Persistence.Read;

public sealed class OrderReportingFact
{
    public Guid OrderId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public string TableLabel { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ServiceMode { get; set; } = "DineIn";
    public string Source { get; set; } = string.Empty;
    public string PaymentTiming { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = "Draft";
    public string PaymentStatus { get; set; } = "Unpaid";
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? RefundedAtUtc { get; set; }
    public DateTime? SaleRecognizedAtUtc { get; set; }
    public string LinesJson { get; set; } = "[]";
    public string StationsJson { get; set; } = "[]";
}
