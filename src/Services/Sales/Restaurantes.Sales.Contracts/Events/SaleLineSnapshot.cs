namespace Restaurantes.Sales.Contracts.Events;

public sealed record SaleLineSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string CategoryName,
    string PreparationStationCode
);
