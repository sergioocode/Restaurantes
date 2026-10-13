namespace Restaurantes.Sales.Contracts.Events;

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
