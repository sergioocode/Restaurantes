namespace Restaurantes.Dining.Domain;

public sealed class DiningSession
{
    public bool RequestGuestCount { get; set; }
    public int? GuestCount { get; set; }
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid? TableId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid? OpenedByUserId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public int Version { get; set; } = 1;
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? CheckoutIdempotencyKey { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal DiscountPercentage { get; set; }
    public string CustomerAccessToken { get; set; } = string.Empty;
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public List<DiningSessionOrder> Orders { get; set; } = [];

    public bool CanCheckout =>
        Status == "Open"
        && Orders.Any(order =>
            order.AmountPaid < ChargeableAmount(order) && order.OrderStatus != "Cancelled"
        );

    public bool CanRelease =>
        Status == "Open"
        && Orders.Count > 0
        && Orders.All(order => order.OrderStatus is "Delivered" or "Cancelled")
        && Orders.All(order =>
            order.AmountPaid >= ChargeableAmount(order) || order.OrderStatus == "Cancelled"
        );

    public decimal ChargeableAmount(DiningSessionOrder order) =>
        Math.Round(
            order.Amount * (1m - DiscountPercentage / 100m),
            2,
            MidpointRounding.AwayFromZero
        );

    public void RecordPayment(Guid idempotencyKey, string method, DateTime now)
    {
        CheckoutIdempotencyKey = idempotencyKey;
        PaymentMethod = method;
        PaidAtUtc = now;
        Version++;
    }

    public void Close(DateTime now)
    {
        Status = "Closed";
        ClosedAtUtc = now;
        Version++;
    }

    public void Cancel(Guid userId, DateTime now)
    {
        Status = "Cancelled";
        CancelledByUserId = userId;
        CancelledAtUtc = now;
        ClosedAtUtc = now;
        Version++;
    }

    public void RegisterGuests(int guestCount)
    {
        GuestCount = guestCount;
        Version++;
    }

    public void ApplyDiscount(decimal percentage)
    {
        DiscountPercentage = percentage;
        Version++;
    }
}
