using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;

namespace Restaurantes.Reporting.Infrastructure.Queries;

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

public sealed class ReportingQueries(ReportingReadDbContext db, TimeProvider time)
{
    public async Task<DailyDashboard> GetDailyAsync(
        DateOnly? requestedDate,
        IReadOnlyCollection<Guid>? restaurantIds,
        int orderLimit,
        CancellationToken ct
    )
    {
        int normalizedOrderLimit = orderLimit is 20 or 100 ? orderLimit : 10;
        DateOnly date = requestedDate ?? DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        DateTime localStart = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified
        );
        DateTime start = TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local);
        DateTime end = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), TimeZoneInfo.Local);
        IQueryable<OrderReportingFact> baseQuery = db.Orders.AsNoTracking();
        if (restaurantIds is not null)
        {
            baseQuery = baseQuery.Where(x => restaurantIds.Contains(x.RestaurantId));
        }

        IQueryable<CompletedSaleFact> salesQuery = db.Sales.AsNoTracking();
        if (restaurantIds is not null)
        {
            salesQuery = salesQuery.Where(x => restaurantIds.Contains(x.RestaurantId));
        }
        List<CompletedSaleFact> sales = await salesQuery
            .Where(x => x.CompletedAtUtc >= start && x.CompletedAtUtc < end)
            .ToListAsync(ct);
        HashSet<Guid> refundedOrderIds = (
            await baseQuery
                .Where(x => x.PaymentStatus == "Refunded")
                .Select(x => x.OrderId)
                .ToListAsync(ct)
        ).ToHashSet();
        sales = sales
            .Where(sale =>
            {
                List<Guid> orderIds = ParseOrderIds(sale);
                return orderIds.Count != 1 || !refundedOrderIds.Contains(orderIds[0]);
            })
            .ToList();
        List<OrderReportingFact> operations = await baseQuery
            .Where(x =>
                (x.CreatedAtUtc >= start && x.CreatedAtUtc < end)
                || x.OrderStatus == "Submitted"
                || x.OrderStatus == "InPreparation"
                || x.OrderStatus == "Ready"
            )
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(normalizedOrderLimit)
            .ToListAsync(ct);
        List<SalesProduct> products = sales
            .SelectMany(ParseSalesLines)
            .GroupBy(x => new { x.ProductId, x.ProductName })
            .Select(g => new SalesProduct(
                g.Key.ProductId,
                g.Key.ProductName,
                g.Sum(x => x.Quantity),
                g.Sum(x => x.UnitPrice * x.Quantity)
            ))
            .OrderByDescending(x => x.Quantity)
            .Take(10)
            .ToList();
        List<SalesRestaurant> restaurants = sales
            .GroupBy(x => x.RestaurantId)
            .Select(g => new SalesRestaurant(g.Key, g.Count(), g.Sum(x => x.Total)))
            .OrderByDescending(x => x.Sales)
            .ToList();
        List<SalesChannel> channels = sales
            .GroupBy(x => x.Source)
            .Select(g => new SalesChannel(g.Key, g.Count(), g.Sum(x => x.Total)))
            .OrderByDescending(x => x.Sales)
            .ToList();
        List<SalesPaymentMethod> paymentMethods = sales
            .GroupBy(x => string.IsNullOrWhiteSpace(x.PaymentMethod) ? "Unknown" : x.PaymentMethod)
            .Select(g => new SalesPaymentMethod(g.Key, g.Count(), g.Sum(x => x.Total)))
            .OrderByDescending(x => x.Sales)
            .ToList();
        decimal total = sales.Sum(x => x.Total);
        return new(
            date,
            total,
            sales.Count,
            sales.Count == 0 ? 0 : total / sales.Count,
            products,
            restaurants,
            channels,
            paymentMethods,
            operations.Select(MapOperation).ToList(),
            time.GetUtcNow().UtcDateTime
        );
    }

    private static List<SalesLine> ParseSalesLines(CompletedSaleFact x)
    {
        return JsonSerializer.Deserialize<List<SalesLine>>(x.LinesJson) ?? [];
    }

    private static List<Guid> ParseOrderIds(CompletedSaleFact x)
    {
        return JsonSerializer.Deserialize<List<Guid>>(x.OrderIdsJson) ?? [];
    }

    private static OperationalOrder MapOperation(OrderReportingFact x)
    {
        List<OperationalStation> stations = (
            JsonSerializer.Deserialize<List<StationJson>>(x.StationsJson) ?? []
        )
            .Select(s => new OperationalStation(s.Code, s.Name, s.Status))
            .ToList();
        List<OperationalLine> lines = (
            JsonSerializer.Deserialize<List<LineJson>>(x.LinesJson) ?? []
        )
            .Select(l => new OperationalLine(
                l.ProductName,
                l.Quantity,
                l.CategoryName,
                l.PreparationStationCode
            ))
            .ToList();
        return new(
            x.OrderId,
            x.RestaurantId,
            x.TableLabel,
            x.CustomerName,
            x.ServiceMode,
            x.Source,
            x.OrderStatus,
            x.PaymentStatus,
            x.PaymentMethod,
            x.Total,
            x.UpdatedAtUtc,
            stations,
            lines
        );
    }

    private sealed record StationJson(
        string Code,
        string Name,
        string Status,
        DateTime? PreparationStartedAtUtc,
        DateTime? ReadyAtUtc
    );

    private sealed record LineJson(
        Guid ProductId,
        string ProductName,
        decimal UnitPrice,
        int Quantity,
        string Notes,
        Guid? CategoryId,
        string CategoryName,
        string PreparationStationCode,
        string PreparationStationName
    );
}
