namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record OrderLineExtraResponse(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal? TaxRate = null
);
