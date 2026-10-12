namespace Restaurantes.Catalog.Contracts.Responses;

public sealed record MenuItemResponse(
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
    DateTime UpdatedAtUtc,
    string DefaultFormat = "PLATO",
    string PhotoSmallUrl = "",
    string PhotoMediumUrl = "",
    string PhotoLargeUrl = "",
    IReadOnlyList<ProductExtraGroup>? ExtraGroups = null,
    IReadOnlyList<MenuItemFormatPrice>? Formats = null
);
