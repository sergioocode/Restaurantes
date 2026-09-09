using Restaurantes.Sales.Contracts.Events;

namespace Restaurantes.Sales.Contracts.Responses;

public sealed record SaleResponse(
    Guid Id,
    Guid RestaurantId,
    string Source,
    string PaymentMethod,
    decimal Total,
    string Status,
    int OrderCount,
    DateTime CompletedAtUtc,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<SaleLineSnapshot> Lines
);
