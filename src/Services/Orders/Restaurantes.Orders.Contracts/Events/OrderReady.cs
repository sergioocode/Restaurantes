namespace Restaurantes.Orders.Contracts.Events;

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
