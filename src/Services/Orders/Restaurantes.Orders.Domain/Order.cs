namespace Restaurantes.Orders.Domain;

public enum OrderStatus
{
    Draft = 1,
    Submitted = 2,
    Cancelled = 3,
    InPreparation = 4,
    Ready = 5,
    Delivered = 6,
}

public enum OrderSource
{
    CustomerQr = 1,
    WaiterMobile = 2,
    Pos = 3,
}

public enum PaymentTiming
{
    Immediate = 1,
    OnAccount = 2,
}

public enum ServiceMode
{
    DineIn = 1,
    Bar = 2,
    Takeaway = 3,
}

public enum OrderLineStatus
{
    Active = 1,
    Cancelled = 2,
}

public sealed record OrderLineDraft(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string Notes,
    Guid? CategoryId,
    string CategoryName,
    string PreparationStationCode,
    string PreparationStationName,
    bool RequiresPrimaryDispatch,
    int Priority
);

public sealed class Order
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderKitchenStation> _stations = [];

    private Order() { }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public int? GuestCount { get; private set; }
    public Guid? TableId { get; private set; }
    public Guid? DiningSessionId { get; private set; }
    public string TableLabel { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public ServiceMode ServiceMode { get; private set; }
    public OrderSource Source { get; private set; }
    public PaymentTiming PaymentTiming { get; private set; }
    public OrderStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? PreparationStartedAtUtc { get; private set; }
    public DateTime? ReadyAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines;
    public IReadOnlyCollection<OrderKitchenStation> Stations => _stations;

    public static Order Create(
        Guid restaurantId,
        Guid? tableId,
        int? guestCount,
        Guid? diningSessionId,
        string tableLabel,
        string customerName,
        ServiceMode serviceMode,
        OrderSource source,
        PaymentTiming paymentTiming,
        IEnumerable<OrderLineDraft> lineDrafts,
        DateTime occurredAtUtc
    )
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId is required.", nameof(restaurantId));
        }

        string normalizedTableLabel = tableLabel.Trim();
        string normalizedCustomerName = customerName.Trim();
        bool hasPhysicalLocation = serviceMode is ServiceMode.DineIn or ServiceMode.Bar;
        if (hasPhysicalLocation && normalizedTableLabel.Length is < 1 or > 80)
        {
            throw new ArgumentException(
                "TableLabel must contain between 1 and 80 characters.",
                nameof(tableLabel)
            );
        }
        if (!hasPhysicalLocation && normalizedTableLabel.Length > 0)
        {
            throw new ArgumentException(
                "TableLabel must be empty for Takeaway orders.",
                nameof(tableLabel)
            );
        }
        if (
            hasPhysicalLocation
            && (tableId is null || diningSessionId is null || diningSessionId == Guid.Empty)
        )
        {
            throw new ArgumentException(
                "DineIn and Bar orders require a physical location and DiningSessionId."
            );
        }
        if (!hasPhysicalLocation && (tableId is not null || diningSessionId is not null))
        {
            throw new ArgumentException(
                "Takeaway orders cannot reference a physical location or dining session."
            );
        }
        if (serviceMode == ServiceMode.Takeaway && normalizedCustomerName.Length is < 1 or > 80)
        {
            throw new ArgumentException(
                "Takeaway orders require a customer name between 1 and 80 characters.",
                nameof(customerName)
            );
        }
        if (serviceMode != ServiceMode.Takeaway && normalizedCustomerName.Length > 0)
        {
            throw new ArgumentException(
                "CustomerName is only used for Takeaway orders.",
                nameof(customerName)
            );
        }

        List<OrderLineDraft> drafts = lineDrafts.ToList();
        if (drafts.Count == 0)
        {
            throw new ArgumentException(
                "An order must contain at least one line.",
                nameof(lineDrafts)
            );
        }

        bool validPaymentFlow = (source, serviceMode, paymentTiming) switch
        {
            (OrderSource.CustomerQr, ServiceMode.DineIn, PaymentTiming.Immediate) => true,
            (OrderSource.CustomerQr, ServiceMode.DineIn, PaymentTiming.OnAccount) => true,
            (OrderSource.WaiterMobile, ServiceMode.DineIn, PaymentTiming.OnAccount) => true,
            (OrderSource.Pos, ServiceMode.DineIn, PaymentTiming.OnAccount) => true,
            (OrderSource.Pos, ServiceMode.Bar, PaymentTiming.OnAccount) => true,
            (OrderSource.Pos, ServiceMode.Takeaway, PaymentTiming.Immediate) => true,
            (OrderSource.Pos, ServiceMode.Takeaway, PaymentTiming.OnAccount) => true,
            _ => false,
        };
        if (!validPaymentFlow)
        {
            throw new ArgumentException(
                "The source, service mode and payment timing combination is invalid."
            );
        }

        Guid orderId = Guid.NewGuid();
        Order order = new()
        {
            Id = orderId,
            RestaurantId = restaurantId,
            TableId = tableId,
            GuestCount = guestCount,
            DiningSessionId = diningSessionId,
            TableLabel = normalizedTableLabel,
            CustomerName = normalizedCustomerName,
            ServiceMode = serviceMode,
            Source = source,
            PaymentTiming = paymentTiming,
            Status = OrderStatus.Draft,
            Version = 1,
            CreatedAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
        };

        foreach (OrderLineDraft draft in drafts)
        {
            order._lines.Add(OrderLine.Create(orderId, draft));
        }
        foreach (
            IGrouping<string, OrderLine> stationLines in order._lines.GroupBy(line =>
                line.PreparationStationCode
            )
        )
        {
            OrderLine firstLine = stationLines.First();
            order._stations.Add(
                OrderKitchenStation.Create(
                    orderId,
                    firstLine.PreparationStationCode,
                    firstLine.PreparationStationName,
                    drafts
                        .First(x =>
                            string.Equals(
                                x.PreparationStationCode,
                                firstLine.PreparationStationCode,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .RequiresPrimaryDispatch,
                    drafts
                        .First(x =>
                            string.Equals(
                                x.PreparationStationCode,
                                firstLine.PreparationStationCode,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Priority
                )
            );
        }

        return order;
    }

    public void Submit(DateTime occurredAtUtc)
    {
        if (Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException("Only draft orders can be submitted.");
        }

        Status = OrderStatus.Submitted;
        SubmittedAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        Version++;
    }

    public void StartStationPreparation(string stationCode, DateTime occurredAtUtc)
    {
        if (Status is not (OrderStatus.Submitted or OrderStatus.InPreparation))
        {
            throw new InvalidOperationException(
                "Only submitted orders or orders already in preparation can start a station."
            );
        }

        OrderKitchenStation station = FindStation(stationCode);
        station.StartPreparation(occurredAtUtc);
        Status = OrderStatus.InPreparation;
        PreparationStartedAtUtc ??= DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        Version++;
    }

    public void MarkStationReady(string stationCode, DateTime occurredAtUtc)
    {
        if (Status is not (OrderStatus.Submitted or OrderStatus.InPreparation))
        {
            throw new InvalidOperationException(
                "Only active kitchen orders can mark a station as ready."
            );
        }

        OrderKitchenStation station = FindStation(stationCode);
        station.MarkReady(occurredAtUtc);
        if (!station.RequiresPrimaryDispatch)
        {
            station.Dispatch(occurredAtUtc);
        }
        PreparationStartedAtUtc ??= station.PreparationStartedAtUtc;
        RecalculateKitchenStatus(occurredAtUtc);
        Version++;
    }

    public void DispatchStation(string stationCode, DateTime occurredAtUtc)
    {
        if (Status is not (OrderStatus.InPreparation or OrderStatus.Ready))
        {
            throw new InvalidOperationException(
                "Only active kitchen orders can dispatch a station."
            );
        }

        OrderKitchenStation station = FindStation(stationCode);
        if (!station.RequiresPrimaryDispatch)
        {
            throw new InvalidOperationException(
                $"Station '{station.Code}' is dispatched automatically when it becomes ready."
            );
        }
        OrderKitchenStation? earlierPass = _stations
            .Where(item =>
                item.Status != KitchenTicketStatus.Cancelled
                && item.RequiresPrimaryDispatch
                && item.Priority < station.Priority
                && item.Status != KitchenTicketStatus.Dispatched
            )
            .OrderBy(item => item.Priority)
            .FirstOrDefault();
        if (earlierPass is not null)
        {
            throw new InvalidOperationException(
                $"Station '{station.Code}' cannot be dispatched before priority {earlierPass.Priority} ({earlierPass.Name})."
            );
        }
        station.Dispatch(occurredAtUtc);
        RecalculateKitchenStatus(occurredAtUtc);
        Version++;
    }

    public void CancelLine(Guid lineId, string reason, DateTime occurredAtUtc)
    {
        if (Status is not (OrderStatus.Draft or OrderStatus.Submitted or OrderStatus.InPreparation))
        {
            throw new InvalidOperationException(
                "Only draft, submitted or partially prepared orders can cancel a line."
            );
        }

        OrderLine line =
            _lines.SingleOrDefault(item => item.Id == lineId)
            ?? throw new InvalidOperationException("The order line does not belong to this order.");
        if (line.Status != OrderLineStatus.Active)
        {
            throw new InvalidOperationException("The order line is already cancelled.");
        }

        OrderKitchenStation station = FindStation(line.PreparationStationCode);
        if (station.Status != KitchenTicketStatus.Pending)
        {
            throw new InvalidOperationException(
                $"The line cannot be cancelled because station ''{station.Code}'' already started preparation."
            );
        }

        DateTime cancelledAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        line.Cancel(reason, cancelledAtUtc);
        if (
            !_lines.Any(item =>
                item.Status == OrderLineStatus.Active && item.PreparationStationCode == station.Code
            )
        )
        {
            station.Cancel();
        }

        List<OrderKitchenStation> activeStations = _stations
            .Where(item => item.Status != KitchenTicketStatus.Cancelled)
            .ToList();
        if (!_lines.Any(item => item.Status == OrderLineStatus.Active))
        {
            Status = OrderStatus.Cancelled;
        }
        else if (Status != OrderStatus.Draft)
        {
            if (activeStations.All(item => item.Status == KitchenTicketStatus.Dispatched))
            {
                Status = OrderStatus.Ready;
                ReadyAtUtc ??= cancelledAtUtc;
            }
            else
            {
                Status = activeStations.Any(item =>
                    item.Status is KitchenTicketStatus.InPreparation or KitchenTicketStatus.Ready
                )
                    ? OrderStatus.InPreparation
                    : OrderStatus.Submitted;
            }
        }
        Version++;
    }

    public void Deliver(DateTime occurredAtUtc)
    {
        if (Status != OrderStatus.Ready)
        {
            throw new InvalidOperationException("Only ready orders can be delivered.");
        }

        Status = OrderStatus.Delivered;
        DeliveredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        Version++;
    }

    public void CancelTakeaway(string reason, DateTime occurredAtUtc)
    {
        if (ServiceMode != ServiceMode.Takeaway)
        {
            throw new InvalidOperationException(
                "Only takeaway orders can use the operational recovery cancellation."
            );
        }
        if (Status is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "Delivered or already cancelled orders cannot be cancelled."
            );
        }

        _ = reason.Trim() is { Length: >= 3 and <= 300 }
            ? true
            : throw new ArgumentException("Cancellation reason must contain 3 to 300 characters.");
        foreach (OrderKitchenStation station in _stations)
        {
            station.CancelForOrder();
        }
        Status = OrderStatus.Cancelled;
        Version++;
    }

    private OrderKitchenStation FindStation(string stationCode)
    {
        string normalizedCode = OrderKitchenStation.NormalizeCode(stationCode);
        return _stations.SingleOrDefault(item => item.Code == normalizedCode)
            ?? throw new InvalidOperationException(
                $"Kitchen station '{normalizedCode}' does not belong to this order."
            );
    }

    private void RecalculateKitchenStatus(DateTime occurredAtUtc)
    {
        List<OrderKitchenStation> activeStations = _stations
            .Where(item => item.Status != KitchenTicketStatus.Cancelled)
            .ToList();
        DateTime utc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        if (activeStations.All(item => item.Status == KitchenTicketStatus.Dispatched))
        {
            ReadyAtUtc ??= utc;
            Status = OrderStatus.Ready;
        }
        else
        {
            Status = OrderStatus.InPreparation;
        }
    }
}

public sealed class OrderLine
{
    private OrderLine() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public string Notes { get; private set; } = string.Empty;
    public Guid? CategoryId { get; private set; }
    public string CategoryName { get; private set; } = string.Empty;
    public string PreparationStationCode { get; private set; } = string.Empty;
    public string PreparationStationName { get; private set; } = string.Empty;
    public OrderLineStatus Status { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string CancellationReason { get; private set; } = string.Empty;

    internal static OrderLine Create(Guid orderId, OrderLineDraft draft)
    {
        if (draft.ProductId == Guid.Empty)
        {
            throw new ArgumentException("ProductId is required.");
        }

        string productName = draft.ProductName.Trim();
        return productName.Length is < 1 or > 160
                ? throw new ArgumentException(
                    "ProductName must contain between 1 and 160 characters."
                )
            : draft.UnitPrice < 0
                ? throw new ArgumentOutOfRangeException(
                    nameof(draft),
                    "UnitPrice cannot be negative."
                )
            : draft.Quantity is < 1 or > 100
                ? throw new ArgumentOutOfRangeException(
                    nameof(draft),
                    "Quantity must be between 1 and 100."
                )
            : new OrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = draft.ProductId,
                ProductName = productName,
                UnitPrice = draft.UnitPrice,
                Quantity = draft.Quantity,
                Notes = draft.Notes.Trim(),
                CategoryId = draft.CategoryId,
                CategoryName = NormalizeLabel(draft.CategoryName, "CategoryName", 120),
                PreparationStationCode = OrderKitchenStation.NormalizeCode(
                    draft.PreparationStationCode
                ),
                PreparationStationName = NormalizeLabel(
                    draft.PreparationStationName,
                    "PreparationStationName",
                    80
                ),
                Status = OrderLineStatus.Active,
            };
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

public enum KitchenTicketStatus
{
    Pending = 1,
    InPreparation = 2,
    Ready = 3,
    Cancelled = 4,
    Dispatched = 5,
}

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
