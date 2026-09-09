namespace Restaurantes.Dining.Domain;

public sealed class DiningSession
{
    public bool RequestGuestCount { get; set; }
    public int? GuestCount { get; set; }
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public int Version { get; set; } = 1;
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? CheckoutIdempotencyKey { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string CustomerAccessToken { get; set; } = string.Empty;
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public List<DiningSessionOrder> Orders { get; set; } = [];

    public bool CanCheckout(bool allowBeforeKitchenCompletion) =>
        Status == "Open"
        && Orders.Count > 0
        && (
            allowBeforeKitchenCompletion
            || Orders.All(x => x.OrderStatus is "Ready" or "Delivered" or "Cancelled")
        );

    public void CompleteCheckout(Guid idempotencyKey, string method, DateTime now)
    {
        CheckoutIdempotencyKey = idempotencyKey;
        PaymentMethod = method;
        PaidAtUtc = now;
        Close(now);
    }

    public void Close(DateTime now)
    {
        Status = "Closed";
        ClosedAtUtc = now;
        Version++;
    }

    public void Cancel(string reason, Guid userId, DateTime now)
    {
        Status = "Cancelled";
        CancellationReason = reason;
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
}
