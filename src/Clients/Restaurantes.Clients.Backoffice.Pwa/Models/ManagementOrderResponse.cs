namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed record ManagementOrderLineResponse(
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    string Status,
    string Format = "",
    decimal? BaseUnitPrice = null,
    IReadOnlyList<ManagementOrderExtraResponse>? Extras = null
);

public sealed record ManagementOrderExtraResponse(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
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
    List<ManagementOrderLineResponse> Lines,
    int? GuestCount = null
);
