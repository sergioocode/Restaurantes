namespace Restaurantes.Orders.Contracts.Events;

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
