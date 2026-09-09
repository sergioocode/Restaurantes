namespace Restaurantes.Catalog.Contracts.Responses;

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    Guid CategoryId,
    decimal BasePrice,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);
