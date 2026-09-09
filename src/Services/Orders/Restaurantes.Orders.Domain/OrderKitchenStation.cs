namespace Restaurantes.Orders.Domain;

public sealed class OrderKitchenStation
{
    private OrderKitchenStation() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public KitchenTicketStatus Status { get; private set; }
    public DateTime? PreparationStartedAtUtc { get; private set; }
    public DateTime? ReadyAtUtc { get; private set; }
    public DateTime? DispatchedAtUtc { get; private set; }
    public bool RequiresPrimaryDispatch { get; private set; }
    public int Priority { get; private set; }

    internal static OrderKitchenStation Create(
        Guid orderId,
        string code,
        string name,
        bool requiresPrimaryDispatch,
        int priority
    )
    {
        return new OrderKitchenStation
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Code = NormalizeCode(code),
            Name = name.Trim(),
            RequiresPrimaryDispatch = requiresPrimaryDispatch,
            Priority = priority is < 1 or > 99
                ? throw new ArgumentOutOfRangeException(nameof(priority))
                : priority,
            Status = KitchenTicketStatus.Pending,
        };
    }

    internal void StartPreparation(DateTime occurredAtUtc)
    {
        if (Status != KitchenTicketStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Station '{Code}' must be pending to start preparation."
            );
        }
        Status = KitchenTicketStatus.InPreparation;
        PreparationStartedAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    internal void MarkReady(DateTime occurredAtUtc)
    {
        if (Status != KitchenTicketStatus.InPreparation)
        {
            throw new InvalidOperationException(
                $"Station '{Code}' must be in preparation before it can be ready."
            );
        }
        Status = KitchenTicketStatus.Ready;
        ReadyAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    internal void Dispatch(DateTime occurredAtUtc)
    {
        if (Status != KitchenTicketStatus.Ready)
        {
            throw new InvalidOperationException(
                $"Station '{Code}' must be ready before it can be dispatched."
            );
        }
        Status = KitchenTicketStatus.Dispatched;
        DispatchedAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    internal void Cancel()
    {
        if (Status != KitchenTicketStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Station ''{Code}'' must be pending before it can be cancelled."
            );
        }
        Status = KitchenTicketStatus.Cancelled;
    }

    internal void CancelForOrder()
    {
        Status = KitchenTicketStatus.Cancelled;
    }

    internal static string NormalizeCode(string value)
    {
        string normalized = value.Trim().ToUpperInvariant();
        return normalized.Length is < 1 or > 40
            || normalized.Any(character =>
                !char.IsLetterOrDigit(character) && character is not ('_' or '-')
            )
                ? throw new ArgumentException(
                    "PreparationStationCode must contain 1 to 40 letters, digits, dashes or underscores."
                )
            : normalized == "CHEF"
                ? throw new ArgumentException(
                    "CHEF is reserved for the overview KDS and cannot prepare product lines."
                )
            : normalized;
    }
}
