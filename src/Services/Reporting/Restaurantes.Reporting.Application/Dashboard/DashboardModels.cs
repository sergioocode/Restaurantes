namespace Restaurantes.Reporting.Application.Dashboard;

public sealed record DailyDashboard(
    DateOnly BusinessDate,
    decimal TotalSales,
    int CompletedSales,
    decimal AverageTicket,
    IReadOnlyList<SalesProduct> TopProducts,
    IReadOnlyList<SalesRestaurant> RestaurantRanking,
    IReadOnlyList<SalesChannel> Channels,
    IReadOnlyList<SalesPaymentMethod> PaymentMethods,
    IReadOnlyList<OperationalOrder> OperationalOrders,
    DateTime GeneratedAtUtc
);

public sealed record SalesProduct(Guid ProductId, string Name, int Quantity, decimal Sales);

public sealed record SalesRestaurant(Guid RestaurantId, int Tickets, decimal Sales);

public sealed record SalesChannel(string Source, int Tickets, decimal Sales);

public sealed record SalesPaymentMethod(string Method, int Tickets, decimal Sales);

public sealed record SalesLine(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string CategoryName,
    string PreparationStationCode
);

public sealed record CompletedSale(
    Guid RestaurantId,
    decimal Total,
    string Source,
    string PaymentMethod,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<SalesLine> Lines
);

public sealed record OperationalOrder(
    Guid OrderId,
    Guid RestaurantId,
    string TableLabel,
    string CustomerName,
    string ServiceMode,
    string Source,
    string OrderStatus,
    string PaymentStatus,
    string PaymentMethod,
    decimal Total,
    DateTime UpdatedAtUtc,
    IReadOnlyList<OperationalStation> Stations,
    IReadOnlyList<OperationalLine> Lines
);

public sealed record OperationalStation(string Code, string Name, string Status);

public sealed record OperationalLine(
    string ProductName,
    int Quantity,
    string CategoryName,
    string PreparationStationCode
);

public sealed record DailyReportingSnapshot(
    IReadOnlyList<CompletedSale> Sales,
    IReadOnlySet<Guid> RefundedOrderIds,
    IReadOnlyList<OperationalOrder> OperationalOrders
);
