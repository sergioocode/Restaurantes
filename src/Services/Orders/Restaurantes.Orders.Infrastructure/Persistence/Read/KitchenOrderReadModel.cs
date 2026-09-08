namespace Restaurantes.Orders.Infrastructure.Persistence.Read;

public sealed class KitchenOrderReadModel
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public int? GuestCount { get; set; }
    public Guid? TableId { get; set; }
    public Guid? DiningSessionId { get; set; }
    public string TableLabel { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ServiceMode { get; set; } = "DineIn";
    public string Source { get; set; } = "Pos";
    public string PaymentTiming { get; set; } = "OnAccount";
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? PreparationStartedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string LinesJson { get; set; } = "[]";
    public string StationsJson { get; set; } = "[]";
}
