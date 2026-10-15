namespace Restaurantes.Catalog.Contracts.Responses;

public sealed record ProductExtraOption(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Price,
    decimal? TaxRate = null
);

public sealed record ProductExtraGroup(
    Guid Id,
    string Name,
    int MinSelections,
    int MaxSelections,
    IReadOnlyList<ProductExtraOption> Options
);
