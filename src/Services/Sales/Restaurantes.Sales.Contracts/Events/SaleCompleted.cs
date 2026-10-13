namespace Restaurantes.Sales.Contracts.Events;

public sealed record SaleCompleted(
    Guid SaleId,
    Guid RestaurantId,
    string Source,
    string PaymentMethod,
    decimal Total,
    DateTime CompletedAtUtc,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<SaleLineSnapshot> Lines,
    DateOnly? BusinessDate = null,
    IReadOnlyList<SalePaymentBreakdown>? PaymentMethods = null,
    decimal TipTotal = 0,
    string? DiscountName = null,
    decimal DiscountPercentage = 0,
    decimal DiscountAmount = 0
);

public sealed record SalePaymentBreakdown(string Method, int Payments, decimal Amount);
