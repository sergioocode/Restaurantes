namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record OrderResponse(
    Guid Id,
    Guid RestaurantId,
    Guid? TableId,
    Guid? DiningSessionId,
    string CustomerName,
    string ServiceMode,
    string PaymentTiming,
    string Status,
    decimal Total,
    DateTime CreatedAtUtc
);
