namespace Restaurantes.Orders.Domain;

public sealed record OrderLineExtra(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
);
