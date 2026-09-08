namespace Restaurantes.Payments.Domain;

public enum PaymentStatus
{
    Open = 1,
    Paid = 2,
    Refunded = 3,
}

public sealed class PayableOrder
{
    private PayableOrder() { }

    public Guid OrderId { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid? TableId { get; private set; }
    public Guid? DiningSessionId { get; private set; }
    public string ServiceMode { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string Method { get; private set; } = string.Empty;
    public string ExternalReference { get; private set; } = string.Empty;
    public Guid? CaptureIdempotencyKey { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CapturedAtUtc { get; private set; }
    public DateTime? RefundedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static PayableOrder Create(
        Guid orderId,
        Guid restaurantId,
        Guid? tableId,
        Guid? diningSessionId,
        string serviceMode,
        string source,
        decimal amount,
        DateTime createdAtUtc
    )
    {
        return orderId == Guid.Empty || restaurantId == Guid.Empty || amount < 0
            ? throw new ArgumentException("Invalid payable order.")
            : new()
            {
                OrderId = orderId,
                RestaurantId = restaurantId,
                TableId = tableId,
                DiningSessionId = diningSessionId,
                ServiceMode = serviceMode,
                Source = source,
                Amount = amount,
                Status = PaymentStatus.Open,
                CreatedAtUtc = createdAtUtc,
                Version = 1,
            };
    }

    public bool Capture(string method, string externalReference, Guid idempotencyKey, DateTime now)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("IdempotencyKey is required.");
        }

        if (Status == PaymentStatus.Paid && CaptureIdempotencyKey == idempotencyKey)
        {
            return false;
        }

        if (Status != PaymentStatus.Open)
        {
            throw new InvalidOperationException("Only open payments can be captured.");
        }

        PaymentId = Guid.NewGuid();
        Method = method.Trim();
        ExternalReference = externalReference.Trim();
        CaptureIdempotencyKey = idempotencyKey;
        Status = PaymentStatus.Paid;
        CapturedAtUtc = now;
        Version++;
        return true;
    }

    public void AdjustAmount(decimal amount)
    {
        if (Status != PaymentStatus.Open)
        {
            throw new InvalidOperationException("Only open payments can change their amount.");
        }
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        if (Amount == amount)
        {
            return;
        }
        Amount = amount;
        Version++;
    }

    public void Refund(DateTime now)
    {
        if (Status != PaymentStatus.Paid)
        {
            throw new InvalidOperationException("Only paid payments can be refunded.");
        }

        Status = PaymentStatus.Refunded;
        RefundedAtUtc = now;
        Version++;
    }
}
