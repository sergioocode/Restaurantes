namespace Restaurantes.Orders.Application;

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
    bool IsAvailable
);
