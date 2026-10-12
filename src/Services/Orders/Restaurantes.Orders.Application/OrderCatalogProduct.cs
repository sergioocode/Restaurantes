namespace Restaurantes.Orders.Application;

public sealed record OrderExtraOption(Guid ProductId, string ProductName, decimal Price);

public sealed record OrderCatalogFormat(string Format, decimal Price);

public sealed record OrderExtraGroup(
    Guid Id,
    string Name,
    int MinSelections,
    int MaxSelections,
    IReadOnlyList<OrderExtraOption> Options
);

public sealed record OrderCatalogProduct(
    Guid RestaurantId,
    Guid ProductId,
    string ProductName,
    decimal Price,
    Guid CategoryId,
    string CategoryName,
    string StationCode,
    string StationName,
    bool RequiresPrimaryDispatch,
    int Priority,
    bool IsAvailable,
    IReadOnlyList<OrderExtraGroup> ExtraGroups,
    string DefaultFormat,
    IReadOnlyList<OrderCatalogFormat> Formats
);
