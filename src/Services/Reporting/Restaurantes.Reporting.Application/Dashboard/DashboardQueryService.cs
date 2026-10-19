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
        List<SalesPaymentMethod> paymentMethods = SummarizePaymentMethods(sales);
        List<OperationalOrder> operationalOrders = EnrichOperationalOrders(
            snapshot.OperationalOrders,
            sales
        );
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

    private static List<SalesPaymentMethod> SummarizePaymentMethods(
        IEnumerable<CompletedSale> sales
    ) =>
        sales
            .SelectMany(PaymentMethodsForSale)
            .GroupBy(payment =>
                string.IsNullOrWhiteSpace(payment.Method) ? "Unknown" : payment.Method
            )
            .Select(group => new SalesPaymentMethod(
                group.Key,
                group.Sum(payment => payment.Tickets),
                group.Sum(payment => payment.Sales)
            ))
            .OrderByDescending(payment => payment.Sales)
            .ToList();

    private static IEnumerable<SalesPaymentMethod> PaymentMethodsForSale(CompletedSale sale)
    {
        if (sale.PaymentMethods.Count > 0)
        {
            return sale.PaymentMethods;
        }

        return [new SalesPaymentMethod(FallbackPaymentMethod(sale.PaymentMethod), 1, sale.Total)];
    }

    private static List<OperationalOrder> EnrichOperationalOrders(
        IReadOnlyList<OperationalOrder> orders,
        IEnumerable<CompletedSale> sales
    )
    {
        Dictionary<Guid, List<string>> methodsByOrder = [];
        foreach (CompletedSale sale in sales)
        {
            List<string> methods = SalePaymentMethodNames(sale);
            foreach (Guid orderId in sale.OrderIds)
            {
                methodsByOrder[orderId] = methods;
            }
        }

        return orders
            .Select(order =>
                order with
                {
                    PaymentMethods = OrderPaymentMethodNames(order, methodsByOrder),
                }
            )
            .ToList();
    }

    private static List<string> SalePaymentMethodNames(CompletedSale sale)
    {
        if (sale.PaymentMethods.Count > 0)
        {
            return sale.PaymentMethods.Select(payment => payment.Method).Distinct().ToList();
        }

        return [FallbackPaymentMethod(sale.PaymentMethod)];
    }

    private static List<string> OrderPaymentMethodNames(
        OperationalOrder order,
        Dictionary<Guid, List<string>> methodsByOrder
    )
    {
        if (methodsByOrder.TryGetValue(order.OrderId, out List<string>? methods))
        {
            return methods;
        }

        return string.IsNullOrWhiteSpace(order.PaymentMethod) ? [] : [order.PaymentMethod];
    }

    private static string FallbackPaymentMethod(string method) =>
        method == "Mixed" ? "UnavailableBreakdown" : method;

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
