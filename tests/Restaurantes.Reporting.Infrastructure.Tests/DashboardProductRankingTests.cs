using Restaurantes.Reporting.Application.Dashboard;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class DashboardProductRankingTests
{
    [Fact]
    public async Task Ranks_each_product_format_separately_and_keeps_only_ten_entries()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        List<SalesLine> lines =
        [
            new(productId, "Pasta", 12m, 4, "Pastas", "", "RACION"),
            new(productId, "Pasta", 10m, 3, "Pastas", "", "PLATO"),
        ];
        lines.AddRange(
            Enumerable
                .Range(0, 10)
                .Select(index => new SalesLine(
                    Guid.NewGuid(),
                    $"Otro {index}",
                    1m,
                    1,
                    "Otros",
                    "",
                    "UNIDAD"
                ))
        );
        CompletedSale sale = new(restaurantId, 88m, "Pos", "Cash", [], [], lines);
        DashboardQueryService service = new(
            new SnapshotStore(new DailyReportingSnapshot([sale], new HashSet<Guid>(), [])),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );

        DailyDashboard dashboard = await service.GetDailyAsync(
            new DateOnly(2026, 10, 11),
            [restaurantId],
            10,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(10, dashboard.TopProducts.Count);
        Assert.Equal("Pasta [Ración]", dashboard.TopProducts[0].Name);
        Assert.Equal(4, dashboard.TopProducts[0].Quantity);
        Assert.Equal(48m, dashboard.TopProducts[0].Sales);
        Assert.Equal("Pasta [Plato]", dashboard.TopProducts[1].Name);
        Assert.Equal(3, dashboard.TopProducts[1].Quantity);
        Assert.Equal(30m, dashboard.TopProducts[1].Sales);
    }

    private sealed class SnapshotStore(DailyReportingSnapshot snapshot) : IReportingReadStore
    {
        public Task<DailyReportingSnapshot> GetDailySnapshotAsync(
            DateOnly businessDate,
            DateTime startUtc,
            DateTime endUtc,
            IReadOnlyCollection<Guid>? restaurantIds,
            int orderLimit,
            CancellationToken ct
        ) => Task.FromResult(snapshot);
    }
}
