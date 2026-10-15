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
        CancellationToken ct
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
        DateTime localStart = DateTime.SpecifyKind(
            businessDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified
        );
        DateTime start = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
        DateTime end = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), timeZone);
        List<Order> orders = await db
            .Orders.AsNoTracking()
            .Where(order =>
                order.RestaurantId == restaurantId
                && (
                    order.BusinessDate == businessDate
                    || (
                        order.BusinessDate == null
                        && order.CreatedAtUtc >= start
                        && order.CreatedAtUtc < end
                    )
                )
            )
            .Where(order =>
                order.Stations.Any(station =>
                    station.Status == KitchenTicketStatus.Dispatched
                    && station.DispatchedAtUtc != null
                    && (
                        configuration.IsPrimary
                            ? station.RequiresPrimaryDispatch
                            : station.Code == configuration.Code
                    )
                )
            )
            .Include(order => order.Stations)
            .Include(order => order.Lines)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ToListAsync(ct);
        return orders
            .Select(order =>
            {
                List<OrderKitchenStation> processedStations = order
                    .Stations.Where(IsProcessedStation)
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
                        .Lines.Where(line =>
                            processedStationCodes.Contains(line.PreparationStationCode)
                        )
                        .Select(line => new KitchenHistoryLineResponse(
                            line.ProductName,
                            line.Quantity,
                            line.Status.ToString(),
                            line.Format
                        ))
                        .ToList()
                );
            })
            .ToList();

        bool IsProcessedStation(OrderKitchenStation station)
        {
            return station.Status == KitchenTicketStatus.Dispatched
                && station.DispatchedAtUtc != null
                && (
                    configuration.IsPrimary
                        ? station.RequiresPrimaryDispatch
                        : station.Code == configuration.Code
                );
        }
    }
}
