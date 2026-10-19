using Restaurantes.Reporting.Application.Dashboard;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class DashboardQueryServicePaymentMethodTests
{
    [Fact]
    public async Task Daily_dashboard_summarizes_explicit_and_fallback_payment_methods()
    {
        DateOnly businessDate = new(2026, 10, 19);
        Guid restaurantId = Guid.NewGuid();
        Guid explicitSaleOrderId = Guid.NewGuid();
        Guid mixedSaleOrderId = Guid.NewGuid();
        Guid simpleSaleOrderId = Guid.NewGuid();
        DashboardSnapshotStore store = new(
            new DailyReportingSnapshot(
                [
                    new CompletedSale(
                        restaurantId,
                        17m,
                        "Pos",
                        "Mixed",
                        [
                            new SalesPaymentMethod("Cash", 1, 5m),
                            new SalesPaymentMethod("Card", 1, 10m),
                            new SalesPaymentMethod("Cash", 1, 2m),
                        ],
                        [explicitSaleOrderId],
                        []
                    ),
                    new CompletedSale(restaurantId, 7m, "Pos", "Mixed", [], [mixedSaleOrderId], []),
                    new CompletedSale(restaurantId, 3m, "Pos", " ", [], [], []),
                    new CompletedSale(restaurantId, 4m, "Pos", "Card", [], [simpleSaleOrderId], []),
                ],
                new HashSet<Guid>(),
                [
                    OperationalOrder(explicitSaleOrderId, "Mixed"),
                    OperationalOrder(mixedSaleOrderId, "Cash"),
                    OperationalOrder(Guid.NewGuid(), "Transfer"),
                    OperationalOrder(Guid.NewGuid(), " "),
                ]
            )
        );
        DashboardQueryService service = new(store, TimeProvider.System, TimeZoneInfo.Utc);

        DailyDashboard dashboard = await service.GetDailyAsync(
            businessDate,
            [restaurantId],
            7,
            CancellationToken.None
        );

        Assert.Equal(31m, dashboard.TotalSales);
        Assert.Equal(4, dashboard.CompletedSales);
        Assert.Equal(businessDate, store.RequestedDate);
        Assert.Equal([restaurantId], store.RequestedRestaurantIds);
        Assert.Equal(10, store.RequestedOrderLimit);
        Assert.Collection(
            dashboard.PaymentMethods.OrderBy(payment => payment.Method),
            payment =>
            {
                Assert.Equal("Card", payment.Method);
                Assert.Equal(2, payment.Tickets);
                Assert.Equal(14m, payment.Sales);
            },
            payment =>
            {
                Assert.Equal("Cash", payment.Method);
                Assert.Equal(2, payment.Tickets);
                Assert.Equal(7m, payment.Sales);
            },
            payment =>
            {
                Assert.Equal("UnavailableBreakdown", payment.Method);
                Assert.Equal(1, payment.Tickets);
                Assert.Equal(7m, payment.Sales);
            },
            payment =>
            {
                Assert.Equal("Unknown", payment.Method);
                Assert.Equal(1, payment.Tickets);
                Assert.Equal(3m, payment.Sales);
            }
        );

        Assert.Equal(
            ["Cash", "Card"],
            dashboard
                .OperationalOrders.Single(order => order.OrderId == explicitSaleOrderId)
                .PaymentMethods
        );
        Assert.Equal(
            ["UnavailableBreakdown"],
            dashboard
                .OperationalOrders.Single(order => order.OrderId == mixedSaleOrderId)
                .PaymentMethods
        );
        Assert.Equal(
            ["Transfer"],
            dashboard
                .OperationalOrders.Single(order => order.PaymentMethod == "Transfer")
                .PaymentMethods
        );
        Assert.Empty(
            dashboard.OperationalOrders.Single(order => order.PaymentMethod == " ").PaymentMethods
        );
    }

    private static OperationalOrder OperationalOrder(Guid orderId, string paymentMethod) =>
        new(
            orderId,
            Guid.NewGuid(),
            "Mesa 1",
            string.Empty,
            "DineIn",
            "Pos",
            "Completed",
            "Paid",
            paymentMethod,
            0m,
            DateTime.UtcNow,
            [],
            [],
            []
        );

    private sealed class DashboardSnapshotStore(DailyReportingSnapshot snapshot)
        : IReportingReadStore
    {
        public DateOnly RequestedDate { get; private set; }
        public IReadOnlyCollection<Guid>? RequestedRestaurantIds { get; private set; }
        public int RequestedOrderLimit { get; private set; }

        public Task<DailyReportingSnapshot> GetDailySnapshotAsync(
            DateOnly businessDate,
            DateTime startUtc,
            DateTime endUtc,
            IReadOnlyCollection<Guid>? restaurantIds,
            int orderLimit,
            CancellationToken ct
        )
        {
            RequestedDate = businessDate;
            RequestedRestaurantIds = restaurantIds;
            RequestedOrderLimit = orderLimit;
            return Task.FromResult(snapshot);
        }
    }
}
