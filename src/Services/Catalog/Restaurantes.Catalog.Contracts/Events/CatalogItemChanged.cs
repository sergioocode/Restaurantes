namespace Restaurantes.Catalog.Contracts.Events;

public sealed record CatalogItemChanged(
    Guid RestaurantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    decimal Price,
    bool IsAvailable,
    string PreparationStationCode,
    string PreparationStationName,
    int Version,
    DateTime OccurredAtUtc
);
