using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Orders.Contracts;

public enum OrderLifecycleStatus
{
    Unknown,
    Draft,
    Submitted,
    InPreparation,
    Ready,
    Delivered,
    Cancelled,
}

public sealed class CreateOrderRequest
{
    public Guid RestaurantId { get; init; }
    public Guid? TableId { get; init; }
    public Guid? DiningSessionId { get; init; }

    [StringLength(80)]
    public string TableLabel { get; init; } = "";

    [StringLength(80)]
    public string CustomerName { get; init; } = "";

    [Required, RegularExpression("^(DineIn|Bar|Takeaway)$")]
    public string ServiceMode { get; init; } = "DineIn";

    [Required, RegularExpression("^(CustomerQr|WaiterMobile|Pos)$")]
    public string Source { get; init; } = "Pos";

    [Required, RegularExpression("^(Immediate|OnAccount)$")]
    public string PaymentTiming { get; init; } = "OnAccount";

    [Required, MinLength(1)]
    public List<CreateOrderLineRequest> Lines { get; init; } = [];
}

public sealed class CreateOrderLineRequest
{
    public Guid ProductId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }

    [StringLength(500)]
    public string Notes { get; init; } = "";
}

public sealed class CancelOrderLineRequest
{
    [Required, StringLength(300, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

public sealed class CancelOrderRequest
{
    [Required, StringLength(300, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

public sealed record OrderLineResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string Notes,
    Guid? CategoryId,
    string CategoryName,
    string PreparationStationCode,
    string PreparationStationName,
    string Status = "Active",
    DateTime? CancelledAtUtc = null,
    string CancellationReason = ""
);

public sealed record OrderKitchenStationResponse(
    string Code,
    string Name,
    string Status,
    bool RequiresPrimaryDispatch,
    int Priority,
    DateTime? PreparationStartedAtUtc,
    DateTime? ReadyAtUtc,
    DateTime? DispatchedAtUtc
);

public sealed record OrderResponse(
    Guid Id,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    Guid? DiningSessionId,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    string Status,
    decimal Total,
    int Version,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime? PreparationStartedAtUtc,
    DateTime? ReadyAtUtc,
    DateTime? DeliveredAtUtc,
    IReadOnlyList<OrderLineResponse> Lines,
    IReadOnlyList<OrderKitchenStationResponse> Stations
);

public sealed record OrderLineSnapshot(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string Notes,
    Guid? CategoryId,
    string CategoryName,
    string PreparationStationCode,
    string PreparationStationName,
    string Status = "Active",
    DateTime? CancelledAtUtc = null,
    string CancellationReason = ""
);

public sealed record OrderKitchenStationSnapshot(
    string Code,
    string Name,
    string Status,
    bool RequiresPrimaryDispatch,
    int Priority,
    DateTime? PreparationStartedAtUtc,
    DateTime? ReadyAtUtc,
    DateTime? DispatchedAtUtc
);

public sealed record OrderCreated(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    Guid? DiningSessionId,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    int Version,
    DateTime OccurredAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record OrderSubmitted(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    int Version,
    DateTime CreatedAtUtc,
    DateTime SubmittedAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record OrderPreparationStarted(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    int Version,
    DateTime CreatedAtUtc,
    DateTime SubmittedAtUtc,
    DateTime PreparationStartedAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record OrderReady(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    int Version,
    DateTime CreatedAtUtc,
    DateTime SubmittedAtUtc,
    DateTime PreparationStartedAtUtc,
    DateTime ReadyAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record OrderDelivered(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    int Version,
    DateTime CreatedAtUtc,
    DateTime SubmittedAtUtc,
    DateTime PreparationStartedAtUtc,
    DateTime ReadyAtUtc,
    DateTime DeliveredAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record OrderCancelled(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    Guid? DiningSessionId,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    string CancellationReason,
    string CancelledBy,
    int Version,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime CancelledAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record OrderLineCancelled(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    Guid? DiningSessionId,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    Guid ChangedLineId,
    string ChangedStationCode,
    string CancellationReason,
    string Status,
    decimal Total,
    int Version,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime CancelledAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record KitchenTicketPreparationStarted(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    string ChangedStationCode,
    int Version,
    DateTime CreatedAtUtc,
    DateTime SubmittedAtUtc,
    DateTime PreparationStartedAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record KitchenTicketReady(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    string ChangedStationCode,
    int Version,
    DateTime CreatedAtUtc,
    DateTime SubmittedAtUtc,
    DateTime PreparationStartedAtUtc,
    DateTime? ReadyAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record KitchenTicketDispatched(
    Guid OrderId,
    Guid RestaurantId,
    Guid? TableId,
    int? GuestCount,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string PaymentTiming,
    string ChangedStationCode,
    int Version,
    DateTime CreatedAtUtc,
    DateTime SubmittedAtUtc,
    DateTime PreparationStartedAtUtc,
    DateTime DispatchedAtUtc,
    IReadOnlyList<OrderLineSnapshot> Lines,
    IReadOnlyList<OrderKitchenStationSnapshot> Stations
);

public sealed record KdsOrderUpdated(
    Guid OrderId,
    Guid RestaurantId,
    string Status,
    int Version,
    DateTime OccurredAtUtc,
    IReadOnlyList<string> StationCodes
);
