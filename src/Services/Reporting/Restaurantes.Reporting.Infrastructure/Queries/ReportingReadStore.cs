using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Contracts.Events;

namespace Restaurantes.Reporting.Infrastructure.Queries;

public sealed class ReportingReadStore(ReportingReadDbContext db) : IReportingReadStore
{
    public async Task<DailyReportingSnapshot> GetDailySnapshotAsync(
        DateOnly businessDate,
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
            .Where(x =>
                x.BusinessDate == businessDate
                || (
                    x.BusinessDate == null
                    && x.CompletedAtUtc >= startUtc
                    && x.CompletedAtUtc < endUtc
                )
            )
            .ToListAsync(ct);
        HashSet<Guid> refundedOrderIds = (
            await orderQuery
                .Where(x => x.PaymentStatus == "Refunded")
                .Select(x => x.OrderId)
                .ToListAsync(ct)
        ).ToHashSet();
        IQueryable<OrderReportingFact> dailyOrders = orderQuery.Where(x =>
            x.CreatedAtUtc >= startUtc && x.CreatedAtUtc < endUtc
        );
        List<Guid> dailyRestaurantIds = await dailyOrders
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToListAsync(ct);
        List<OrderReportingFact> orderFacts = [];
        foreach (Guid restaurantId in dailyRestaurantIds)
        {
            orderFacts.AddRange(
                await dailyOrders
                    .Where(x => x.RestaurantId == restaurantId)
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .ThenByDescending(x => x.OrderId)
                    .Take(orderLimit)
                    .ToListAsync(ct)
            );
        }
        orderFacts = orderFacts
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.OrderId)
            .ToList();
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
            (JsonSerializer.Deserialize<List<SalePaymentBreakdown>>(fact.PaymentMethodsJson) ?? [])
                .Select(payment => new SalesPaymentMethod(
                    payment.Method,
                    payment.Payments,
                    payment.Amount
                ))
                .ToList(),
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
                x.PreparationStationCode,
                x.KitchenStatus
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
        string PreparationStationName,
        string KitchenStatus
    );
}
