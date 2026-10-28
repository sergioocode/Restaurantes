using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Infrastructure.Persistence.Read;

namespace Restaurantes.Orders.Infrastructure.Stores;

public sealed class KitchenOrderReadStore(OrderReadDbContext dbContext) : IKitchenOrderReadStore
{
    public async Task<OrderResponse?> FindAsync(Guid id, CancellationToken ct)
    {
        KitchenOrderReadModel? item = await dbContext
            .KitchenOrders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == id, ct);
        return item is null ? null : Map(item, null);
    }

    public async Task<IReadOnlyList<OrderResponse>> ListAsync(
        Guid restaurantId,
        string? stationCode,
        CancellationToken ct,
        Guid? zoneId = null
    )
    {
        List<KitchenOrderReadModel> items = await dbContext
            .KitchenOrders.AsNoTracking()
            .Where(item =>
                item.RestaurantId == restaurantId
                && (
                    zoneId == null
                    || (zoneId == Guid.Empty ? item.ZoneId == null : item.ZoneId == zoneId)
                )
                && (
                    item.Status == "Submitted"
                    || item.Status == "InPreparation"
                    || item.Status == "Ready"
                )
            )
            .OrderBy(item => item.SubmittedAtUtc)
            .ToListAsync(ct);
        string? normalizedStationCode = string.IsNullOrWhiteSpace(stationCode)
            ? null
            : stationCode.Trim().ToUpperInvariant();
        return items
            .Select(item => Map(item, normalizedStationCode))
            .OfType<OrderResponse>()
            .ToList();
    }

    private static OrderResponse? Map(KitchenOrderReadModel item, string? stationCode)
    {
        List<OrderLineResponse> lines =
            JsonSerializer.Deserialize<List<OrderLineResponse>>(item.LinesJson) ?? [];
        List<OrderKitchenStationResponse> stations =
            JsonSerializer.Deserialize<List<OrderKitchenStationResponse>>(item.StationsJson) ?? [];
        if (!string.IsNullOrWhiteSpace(stationCode))
        {
            if (
                !TryFilterForStation(
                    item,
                    stationCode,
                    lines,
                    stations,
                    out List<OrderLineResponse> filteredLines,
                    out List<OrderKitchenStationResponse> filteredStations
                )
            )
            {
                return null;
            }

            lines = filteredLines;
            stations = filteredStations;
        }

        return new(
            item.Id,
            item.RestaurantId,
            item.TableId,
            item.GuestCount,
            item.DiningSessionId,
            item.TableLabel,
            item.CustomerName,
            item.ServiceMode,
            item.Source,
            item.PaymentTiming,
            item.Status,
            item.Total,
            item.Version,
            item.CreatedAtUtc,
            item.SubmittedAtUtc,
            item.PreparationStartedAtUtc,
            item.ReadyAtUtc,
            item.DeliveredAtUtc,
            lines,
            stations,
            item.ZoneId
        );
    }

    private static bool TryFilterForStation(
        KitchenOrderReadModel item,
        string stationCode,
        List<OrderLineResponse> lines,
        List<OrderKitchenStationResponse> stations,
        out List<OrderLineResponse> filteredLines,
        out List<OrderKitchenStationResponse> filteredStations
    )
    {
        filteredStations = stations;
        if (stationCode == "CHEF")
        {
            filteredLines = lines
                .Where(line =>
                    line.Status != "Cancelled"
                    && !string.IsNullOrWhiteSpace(line.PreparationStationCode)
                )
                .ToList();
            bool fullyDispatched = stations
                .Where(station => station.Status != "Cancelled")
                .All(station => station.Status == "Dispatched");
            return filteredLines.Count > 0 && !(item.Status == "Ready" && fullyDispatched);
        }

        filteredLines = lines
            .Where(line => line.Status != "Cancelled" && line.PreparationStationCode == stationCode)
            .ToList();
        OrderKitchenStationResponse? selected = stations.FirstOrDefault(station =>
            station.Code == stationCode
        );
        if (selected is null || selected.Status is "Dispatched" or "Cancelled")
        {
            filteredStations = [];
            return false;
        }

        filteredStations = stations.Where(station => station.Code == stationCode).ToList();
        return filteredLines.Count > 0;
    }
}
