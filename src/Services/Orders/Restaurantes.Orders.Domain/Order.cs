namespace Restaurantes.Orders.Domain;

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
