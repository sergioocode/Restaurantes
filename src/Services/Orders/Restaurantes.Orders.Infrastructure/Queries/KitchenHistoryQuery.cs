using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Infrastructure.Queries;

public sealed class KitchenHistoryQuery(OrderWriteDbContext db, TimeZoneInfo timeZone)
    : IKitchenHistoryQuery
{
    public async Task<IReadOnlyList<KitchenHistoryResponse>?> ListAsync(
        Guid restaurantId,
        DateOnly businessDate,
        string stationCode,
        CancellationToken ct,
        Guid? zoneId = null
    )
    {
        OrderKitchenStationConfiguration? configuration = await db
            .KitchenStationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(
                station =>
                    station.RestaurantId == restaurantId
                    && (stationCode == "CHEF" ? station.IsPrimary : station.Code == stationCode),
                ct
            );
        if (configuration is null || !configuration.IsActive)
        {
            return null;
        }

        (DateTime start, DateTime end) = GetBusinessDayUtcRange(businessDate);
        List<Order> orders = await GetDispatchedOrdersAsync(
            restaurantId,
            businessDate,
            zoneId,
            start,
            end,
            configuration,
            ct
        );
        return orders.Select(order => ToHistoryResponse(order, configuration)).ToList();
    }

    private (DateTime Start, DateTime End) GetBusinessDayUtcRange(DateOnly businessDate)
    {
        DateTime localStart = DateTime.SpecifyKind(
            businessDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified
        );
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone),
            TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), timeZone)
        );
    }

    private async Task<List<Order>> GetDispatchedOrdersAsync(
        Guid restaurantId,
        DateOnly businessDate,
        Guid? zoneId,
        DateTime start,
        DateTime end,
        OrderKitchenStationConfiguration configuration,
        CancellationToken ct
    ) =>
        await db
            .Orders.AsNoTracking()
            .Where(order => order.RestaurantId == restaurantId)
            .Where(HasZone(zoneId))
            .Where(HasBusinessDate(businessDate, start, end))
            .Where(HasDispatchedStation(configuration))
            .AsSplitQuery()
            .Include(order => order.Stations)
            .Include(order => order.Lines)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ToListAsync(ct);

    private static Expression<Func<Order, bool>> HasZone(Guid? zoneId) =>
        order =>
            zoneId == null
            || (zoneId == Guid.Empty ? order.ZoneId == null : order.ZoneId == zoneId);

    private static Expression<Func<Order, bool>> HasBusinessDate(
        DateOnly businessDate,
        DateTime start,
        DateTime end
    ) =>
        order =>
            order.BusinessDate == businessDate
            || (
                order.BusinessDate == null
                && order.CreatedAtUtc >= start
                && order.CreatedAtUtc < end
            );

    private static Expression<Func<Order, bool>> HasDispatchedStation(
        OrderKitchenStationConfiguration configuration
    ) =>
        order =>
            order.Stations.Any(station =>
                station.Status == KitchenTicketStatus.Dispatched
                && station.DispatchedAtUtc != null
                && (
                    configuration.IsPrimary
                        ? station.RequiresPrimaryDispatch
                        : station.Code == configuration.Code
                )
            );

    private static KitchenHistoryResponse ToHistoryResponse(
        Order order,
        OrderKitchenStationConfiguration configuration
    )
    {
        List<OrderKitchenStation> processedStations = order
            .Stations.Where(station => IsProcessedStation(station, configuration))
            .ToList();
        HashSet<string> processedStationCodes = processedStations
            .Select(station => station.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new KitchenHistoryResponse(
            order.Id,
            order.TableLabel,
            order.CustomerName,
            order.CashRegisterSessionId,
            order.Status.ToString(),
            order.CreatedAtUtc,
            processedStations.Min(station => station.DispatchedAtUtc),
            order
                .Lines.Where(line => processedStationCodes.Contains(line.PreparationStationCode))
                .Select(line => new KitchenHistoryLineResponse(
                    line.ProductName,
                    line.Quantity,
                    line.Status.ToString(),
                    line.Format
                ))
                .ToList()
        );
    }

    private static bool IsProcessedStation(
        OrderKitchenStation station,
        OrderKitchenStationConfiguration configuration
    ) =>
        station.Status == KitchenTicketStatus.Dispatched
        && station.DispatchedAtUtc != null
        && (
            configuration.IsPrimary
                ? station.RequiresPrimaryDispatch
                : station.Code == configuration.Code
        );
}
