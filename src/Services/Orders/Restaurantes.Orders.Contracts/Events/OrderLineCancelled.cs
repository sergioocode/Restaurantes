namespace Restaurantes.Orders.Contracts.Events;

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
