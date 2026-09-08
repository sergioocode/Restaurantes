namespace Restaurantes.Sales.Application;

public sealed record DeliveredOrderSnapshot(
    Guid OrderId,
    Guid RestaurantId,
    DateTime DeliveredAtUtc,
    string LinesJson
);
