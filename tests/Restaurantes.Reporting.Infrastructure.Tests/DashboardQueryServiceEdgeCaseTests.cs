using Restaurantes.Reporting.Application.Dashboard;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class DashboardQueryServiceEdgeCaseTests
{
    [Fact]
    public async Task Missing_date_uses_local_business_day_and_empty_sales_have_zero_average()
    {
        DateTimeOffset now = new(2026, 10, 18, 23, 30, 0, TimeSpan.Zero);
        SnapshotStore store = new(new DailyReportingSnapshot([], new HashSet<Guid>(), []));
        TimeZoneInfo timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "restaurant-time-zone",
            TimeSpan.FromHours(2),
            "Restaurant time zone",
            "Restaurant time zone"
        );
        DashboardQueryService service = new(store, new FixedTimeProvider(now), timeZone);

        DailyDashboard dashboard = await service.GetDailyAsync(
            null,
            null,
            20,
            CancellationToken.None
        );

        Assert.Equal(new DateOnly(2026, 10, 19), dashboard.BusinessDate);
        Assert.Equal(new DateTime(2026, 10, 18, 22, 0, 0, DateTimeKind.Utc), store.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 19, 22, 0, 0, DateTimeKind.Utc), store.EndUtc);
        Assert.Equal(20, store.OrderLimit);
        Assert.Equal(0m, dashboard.AverageTicket);
        Assert.Empty(dashboard.TopProducts);
        Assert.Empty(dashboard.PaymentMethods);
    }

    [Fact]
    public async Task Refunds_exclude_only_single_order_sales_and_invalid_limits_use_default()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid refundedOrderId = Guid.NewGuid();
        Guid mixedSaleSecondOrderId = Guid.NewGuid();
        Guid noOrderSaleId = Guid.NewGuid();
        SnapshotStore store = new(
            new DailyReportingSnapshot(
                [
                    new CompletedSale(restaurantId, 9m, "Pos", "Cash", [], [refundedOrderId], []),
                    new CompletedSale(
                        restaurantId,
                        15m,
                        "Comandero",
                        "Mixed",
                        [],
                        [refundedOrderId, mixedSaleSecondOrderId],
                        []
                    ),
                    new CompletedSale(restaurantId, 3m, "Pos", "Card", [], [], []),
                ],
                new HashSet<Guid> { refundedOrderId },
                [OperationalOrder(mixedSaleSecondOrderId), OperationalOrder(noOrderSaleId)]
            )
        );
        DashboardQueryService service = new(
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)),
            TimeZoneInfo.Utc
        );

        DailyDashboard dashboard = await service.GetDailyAsync(
            new DateOnly(2026, 10, 19),
            [restaurantId],
            7,
            CancellationToken.None
        );

        Assert.Equal(18m, dashboard.TotalSales);
        Assert.Equal(2, dashboard.CompletedSales);
        Assert.Equal(9m, dashboard.AverageTicket);
        Assert.Equal(10, store.OrderLimit);
        Assert.Contains(
            dashboard.PaymentMethods,
            payment => payment.Method == "UnavailableBreakdown"
        );
        Assert.Contains(dashboard.PaymentMethods, payment => payment.Method == "Card");
        Assert.Equal(
            ["UnavailableBreakdown"],
            Assert
                .Single(
                    dashboard.OperationalOrders,
                    order => order.OrderId == mixedSaleSecondOrderId
                )
                .PaymentMethods
        );
        Assert.Empty(
            Assert
                .Single(dashboard.OperationalOrders, order => order.OrderId == noOrderSaleId)
                .PaymentMethods
        );
    }

    private static OperationalOrder OperationalOrder(Guid orderId) =>
        new(
            orderId,
            Guid.NewGuid(),
            "",
            "",
            "DineIn",
            "Pos",
            "Completed",
            "Paid",
            "",
            0m,
            DateTime.UtcNow,
            [],
            [],
            []
        );

    private sealed class SnapshotStore(DailyReportingSnapshot snapshot) : IReportingReadStore
    {
        public DateTime StartUtc { get; private set; }
        public DateTime EndUtc { get; private set; }
        public int OrderLimit { get; private set; }

        public Task<DailyReportingSnapshot> GetDailySnapshotAsync(
            DateOnly businessDate,
            DateTime startUtc,
            DateTime endUtc,
            IReadOnlyCollection<Guid>? restaurantIds,
            int orderLimit,
            CancellationToken ct
        )
        {
            StartUtc = startUtc;
            EndUtc = endUtc;
            OrderLimit = orderLimit;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
