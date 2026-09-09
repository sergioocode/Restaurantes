namespace Restaurantes.Orders.Contracts.Events;

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
