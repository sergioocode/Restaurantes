namespace Restaurantes.Reporting.Application.Dashboard;

public interface IReportingReadStore
{
    Task<DailyReportingSnapshot> GetDailySnapshotAsync(
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyCollection<Guid>? restaurantIds,
        int orderLimit,
        CancellationToken ct
    );
}
