namespace Restaurantes.Reporting.Application.Dashboard;

public sealed class DashboardQueryService(
    IReportingReadStore store,
    TimeProvider time,
    TimeZoneInfo timeZone
)
{
    public async Task<DailyDashboard> GetDailyAsync(
        DateOnly? requestedDate,
        IReadOnlyCollection<Guid>? restaurantIds,
        int orderLimit,
        CancellationToken ct
    )
    {
        int normalizedOrderLimit = orderLimit is 20 or 100 ? orderLimit : 10;
        DateOnly date =
            requestedDate
            ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), timeZone).DateTime);
        DateTime localStart = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified
        );
        DateTime start = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
        DateTime end = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), timeZone);
        DailyReportingSnapshot snapshot = await store.GetDailySnapshotAsync(
            date,
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
            .GroupBy(x => new
            {
                x.ProductId,
                x.ProductName,
                x.Format,
            })
            .Select(g => new SalesProduct(
                g.Key.ProductId,
                string.IsNullOrWhiteSpace(g.Key.Format)
                    ? g.Key.ProductName
                    : $"{g.Key.ProductName} [{FormatLabel(g.Key.Format)}]",
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
            .SelectMany(sale =>
                sale.PaymentMethods.Count > 0
                    ? sale.PaymentMethods
                    :
                    [
                        new SalesPaymentMethod(
                            sale.PaymentMethod == "Mixed"
                                ? "UnavailableBreakdown"
                                : sale.PaymentMethod,
                            1,
                            sale.Total
                        ),
                    ]
            )
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Method) ? "Unknown" : x.Method)
            .Select(g => new SalesPaymentMethod(g.Key, g.Sum(x => x.Tickets), g.Sum(x => x.Sales)))
            .OrderByDescending(x => x.Sales)
            .ToList();
        Dictionary<Guid, IReadOnlyList<string>> salePaymentMethodsByOrder = [];
        foreach (CompletedSale sale in sales)
        {
            IReadOnlyList<string> methods =
                sale.PaymentMethods.Count > 0
                    ? sale.PaymentMethods.Select(payment => payment.Method).Distinct().ToList()
                    : [sale.PaymentMethod == "Mixed" ? "UnavailableBreakdown" : sale.PaymentMethod];
            foreach (Guid orderId in sale.OrderIds)
            {
                salePaymentMethodsByOrder[orderId] = methods;
            }
        }
        List<OperationalOrder> operationalOrders = snapshot
            .OperationalOrders.Select(order =>
                order with
                {
                    PaymentMethods =
                        salePaymentMethodsByOrder.TryGetValue(
                            order.OrderId,
                            out IReadOnlyList<string>? methods
                        )
                            ? methods
                        : string.IsNullOrWhiteSpace(order.PaymentMethod) ? []
                        : [order.PaymentMethod],
                }
            )
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
            operationalOrders,
            time.GetUtcNow().UtcDateTime
        );
    }

    private static string FormatLabel(string format) =>
        format switch
        {
            "PLATO" => "Plato",
            "RACION" => "Ración",
            "MEDIA_RACION" => "Media ración",
            "TAPA" => "Tapa",
            "UNIDAD" => "Unidad",
            "TERCIO" => "Tercio",
            "VASO" => "Vaso",
            "COPA" => "Copa",
            _ => format,
        };
}
