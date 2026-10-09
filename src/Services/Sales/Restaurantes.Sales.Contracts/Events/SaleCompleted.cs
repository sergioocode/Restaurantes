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
    IReadOnlyList<SalePaymentBreakdown>? PaymentMethods = null
);

public sealed record SalePaymentBreakdown(string Method, int Payments, decimal Amount);
