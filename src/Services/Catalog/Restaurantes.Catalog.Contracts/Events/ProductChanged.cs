namespace Restaurantes.Catalog.Contracts.Events;

public sealed record ProductChanged(
    Guid ProductId,
    string Sku,
    string Name,
    Guid CategoryId,
    decimal BasePrice,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);
