namespace Restaurantes.Orders.Contracts.Events;

public sealed record OrderLineExtraSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal? TaxRate = null
);
