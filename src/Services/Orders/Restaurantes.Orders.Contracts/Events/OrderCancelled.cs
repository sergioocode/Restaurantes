namespace Restaurantes.Orders.Contracts.Events;

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
