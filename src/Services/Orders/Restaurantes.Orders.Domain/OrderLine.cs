namespace Restaurantes.Orders.Domain;

public sealed class OrderLine
{
    private OrderLine() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string Format { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public decimal BaseUnitPrice { get; private set; }
    public decimal? TaxRate { get; private set; }
    public string ExtrasJson { get; private set; } = "[]";

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyList<OrderLineExtra> Extras =>
        System.Text.Json.JsonSerializer.Deserialize<List<OrderLineExtra>>(ExtrasJson) ?? [];
    public int Quantity { get; private set; }
    public string Notes { get; private set; } = string.Empty;
    public Guid? CategoryId { get; private set; }
    public string CategoryName { get; private set; } = string.Empty;
    public string PreparationStationCode { get; private set; } = string.Empty;
    public string PreparationStationName { get; private set; } = string.Empty;
    public OrderLineStatus Status { get; private set; }
    public KitchenLineStatus KitchenStatus { get; private set; }
    public DateTime? PreparationStartedAtUtc { get; private set; }
    public DateTime? ReadyAtUtc { get; private set; }
    public DateTime? DispatchedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string CancellationReason { get; private set; } = string.Empty;

    internal static OrderLine Create(Guid orderId, OrderLineDraft draft)
    {
        if (draft.ProductId == Guid.Empty)
        {
            throw new ArgumentException("ProductId is required.");
        }

        OrderLineExtra[] extras = draft.Extras?.ToArray() ?? [];
        decimal extrasUnitPrice = extras.Sum(extra => extra.UnitPrice * extra.Quantity);
        decimal baseUnitPrice = draft.BaseUnitPrice ?? draft.UnitPrice - extrasUnitPrice;
        ValidatePriceDetails(baseUnitPrice, extrasUnitPrice, draft.UnitPrice, extras);

        string productName = draft.ProductName.Trim();
        ValidateDraft(draft, productName);

        return new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductId = draft.ProductId,
            ProductName = productName,
            Format = draft.Format,
            UnitPrice = draft.UnitPrice,
            BaseUnitPrice = baseUnitPrice,
            TaxRate = draft.TaxRate,
            ExtrasJson = System.Text.Json.JsonSerializer.Serialize(extras),
            Quantity = draft.Quantity,
            Notes = draft.Notes.Trim(),
            CategoryId = draft.CategoryId,
            CategoryName = NormalizeLabel(draft.CategoryName, "CategoryName", 120),
            PreparationStationCode = draft.RequiresKds
                ? OrderKitchenStation.NormalizeCode(draft.PreparationStationCode)
                : "",
            PreparationStationName = draft.RequiresKds
                ? NormalizeLabel(draft.PreparationStationName, "PreparationStationName", 80)
                : "",
            Status = OrderLineStatus.Active,
            KitchenStatus = KitchenLineStatus.Pending,
        };
    }

    private static void ValidatePriceDetails(
        decimal baseUnitPrice,
        decimal extrasUnitPrice,
        decimal unitPrice,
        OrderLineExtra[] extras
    )
    {
        bool hasInvalidExtra = extras.Any(extra =>
            extra.ProductId == Guid.Empty
            || string.IsNullOrWhiteSpace(extra.ProductName)
            || extra.UnitPrice < 0
            || extra.Quantity is < 1 or > 99
            || extra.TaxRate is < 0 or > 100
        );

        if (baseUnitPrice < 0 || baseUnitPrice + extrasUnitPrice != unitPrice || hasInvalidExtra)
        {
            throw new ArgumentException("Order line extra prices are invalid.");
        }
    }

    private static void ValidateDraft(OrderLineDraft draft, string productName)
    {
        if (productName.Length is < 1 or > 160)
        {
            throw new ArgumentException("ProductName must contain between 1 and 160 characters.");
        }

        if (draft.UnitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), "UnitPrice cannot be negative.");
        }

        if (draft.TaxRate is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), "TaxRate is invalid.");
        }

        if (draft.Quantity is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(draft),
                "Quantity must be between 1 and 100."
            );
        }
    }

    internal void MarkDirectlyReadyAndDispatched(DateTime occurredAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(PreparationStationCode))
        {
            throw new InvalidOperationException("KDS lines cannot use direct fulfillment.");
        }
        if (KitchenStatus != KitchenLineStatus.Pending)
        {
            throw new InvalidOperationException("Only pending direct lines can be fulfilled.");
        }

        DateTime utc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        KitchenStatus = KitchenLineStatus.Dispatched;
        ReadyAtUtc = utc;
        DispatchedAtUtc = utc;
    }

    internal void StartPreparation(DateTime occurredAtUtc)
    {
        if (KitchenStatus != KitchenLineStatus.Pending)
        {
            throw new InvalidOperationException(
                "The product line must be pending to start preparation."
            );
        }

        KitchenStatus = KitchenLineStatus.InPreparation;
        PreparationStartedAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    internal void MarkReady(DateTime occurredAtUtc)
    {
        if (KitchenStatus != KitchenLineStatus.InPreparation)
        {
            throw new InvalidOperationException(
                "The product line must be in preparation before it can be ready."
            );
        }

        KitchenStatus = KitchenLineStatus.Ready;
        ReadyAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    internal void Dispatch(DateTime occurredAtUtc)
    {
        if (KitchenStatus != KitchenLineStatus.Ready)
        {
            throw new InvalidOperationException(
                "The product line must be ready before it can be dispatched."
            );
        }

        KitchenStatus = KitchenLineStatus.Dispatched;
        DispatchedAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    internal void Cancel(string reason, DateTime occurredAtUtc)
    {
        string normalizedReason = reason.Trim();
        if (normalizedReason.Length is < 3 or > 300)
        {
            throw new ArgumentException("Cancellation reason must contain 3 to 300 characters.");
        }
        Status = OrderLineStatus.Cancelled;
        CancelledAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        CancellationReason = normalizedReason;
    }

    private static string NormalizeLabel(string value, string field, int maxLength)
    {
        string normalized = value.Trim();
        return normalized.Length is < 1 || normalized.Length > maxLength
            ? throw new ArgumentException(
                $"{field} must contain between 1 and {maxLength} characters."
            )
            : normalized;
    }
}
