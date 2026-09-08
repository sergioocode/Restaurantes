namespace Restaurantes.Sales.Contracts;

public sealed record SaleLineSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string CategoryName,
    string PreparationStationCode
);

public sealed record SaleCompleted(
    Guid SaleId,
    Guid RestaurantId,
    string Source,
    string PaymentMethod,
    decimal Total,
    DateTime CompletedAtUtc,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<SaleLineSnapshot> Lines
);

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
