namespace Restaurantes.Reporting.Application.Dashboard;

public sealed class DashboardQueryService(IReportingReadStore store, TimeProvider time)
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
        DailyReportingSnapshot snapshot = await store.GetDailySnapshotAsync(
            start,
            end,
            restaurantIds,
            normalizedOrderLimit,
            ct
        );
        List<CompletedSale> sales = snapshot
            .Sales.Where(sale =>
                sale.OrderIds.Count != 1 || !snapshot.RefundedOrderIds.Contains(sale.OrderIds[0])
            )
            .ToList();
        List<SalesProduct> products = sales
            .SelectMany(x => x.Lines)
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
            snapshot.OperationalOrders,
            time.GetUtcNow().UtcDateTime
        );
    }
}
