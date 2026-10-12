namespace Restaurantes.Orders.Contracts.Responses;

public sealed record ManagementOrderLineResponse(
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    string Status,
    string Format = ""
);

public sealed record ManagementOrderResponse(
    Guid Id,
    Guid RestaurantId,
    string TableLabel,
    string CustomerName,
    string Source,
    string Status,
    decimal Total,
    DateTime CreatedAtUtc,
    DateTimeOffset CreatedAtLocal,
    string? PaymentMethod,
    DateTimeOffset? PaymentCapturedAtLocal,
    IReadOnlyList<ManagementOrderLineResponse> Lines,
    int? GuestCount = null
);
