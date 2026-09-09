using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;

namespace Restaurantes.Reporting.Infrastructure.Queries;

public sealed class ReportingReadStore(ReportingReadDbContext db) : IReportingReadStore
{
    public async Task<DailyReportingSnapshot> GetDailySnapshotAsync(
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyCollection<Guid>? restaurantIds,
        int orderLimit,
        CancellationToken ct
    )
    {
        IQueryable<OrderReportingFact> orderQuery = db.Orders.AsNoTracking();
        IQueryable<CompletedSaleFact> salesQuery = db.Sales.AsNoTracking();
        if (restaurantIds is not null)
        {
            orderQuery = orderQuery.Where(x => restaurantIds.Contains(x.RestaurantId));
            salesQuery = salesQuery.Where(x => restaurantIds.Contains(x.RestaurantId));
        }

        List<CompletedSaleFact> saleFacts = await salesQuery
            .Where(x => x.CompletedAtUtc >= startUtc && x.CompletedAtUtc < endUtc)
            .ToListAsync(ct);
        HashSet<Guid> refundedOrderIds = (
            await orderQuery
                .Where(x => x.PaymentStatus == "Refunded")
                .Select(x => x.OrderId)
                .ToListAsync(ct)
        ).ToHashSet();
        List<OrderReportingFact> orderFacts = await orderQuery
            .Where(x =>
                (x.CreatedAtUtc >= startUtc && x.CreatedAtUtc < endUtc)
                || x.OrderStatus == "Submitted"
                || x.OrderStatus == "InPreparation"
                || x.OrderStatus == "Ready"
            )
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(orderLimit)
            .ToListAsync(ct);
        return new(
            saleFacts.Select(MapSale).ToList(),
            refundedOrderIds,
            orderFacts.Select(MapOperation).ToList()
        );
    }

    private static CompletedSale MapSale(CompletedSaleFact fact)
    {
        return new(
            fact.RestaurantId,
            fact.Total,
            fact.Source,
            fact.PaymentMethod,
            JsonSerializer.Deserialize<List<Guid>>(fact.OrderIdsJson) ?? [],
            JsonSerializer.Deserialize<List<SalesLine>>(fact.LinesJson) ?? []
        );
    }

    private static OperationalOrder MapOperation(OrderReportingFact fact)
    {
        List<OperationalStation> stations = (
            JsonSerializer.Deserialize<List<StationJson>>(fact.StationsJson) ?? []
        )
            .Select(x => new OperationalStation(x.Code, x.Name, x.Status))
            .ToList();
        List<OperationalLine> lines = (
            JsonSerializer.Deserialize<List<LineJson>>(fact.LinesJson) ?? []
        )
            .Select(x => new OperationalLine(
                x.ProductName,
                x.Quantity,
                x.CategoryName,
                x.PreparationStationCode
            ))
            .ToList();
        return new(
            fact.OrderId,
            fact.RestaurantId,
            fact.TableLabel,
            fact.CustomerName,
            fact.ServiceMode,
            fact.Source,
            fact.OrderStatus,
            fact.PaymentStatus,
            fact.PaymentMethod,
            fact.Total,
            fact.UpdatedAtUtc,
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
