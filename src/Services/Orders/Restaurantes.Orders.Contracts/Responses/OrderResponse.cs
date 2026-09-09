namespace Restaurantes.Orders.Contracts.Responses;

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
