namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed record SaleLineSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string CategoryName,
    string PreparationStationCode,
    string Format = "",
    decimal? BaseUnitPrice = null,
    IReadOnlyList<SaleLineExtraSnapshot>? Extras = null
);

public sealed record SaleLineExtraSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
);

public sealed record SalePaymentBreakdown(string Method, int Payments, decimal Amount);

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
    IReadOnlyList<SaleLineSnapshot> Lines,
    decimal TipTotal = 0,
    IReadOnlyList<SalePaymentBreakdown>? PaymentMethods = null,
    string? DiscountName = null,
    decimal DiscountPercentage = 0,
    decimal DiscountAmount = 0
);
