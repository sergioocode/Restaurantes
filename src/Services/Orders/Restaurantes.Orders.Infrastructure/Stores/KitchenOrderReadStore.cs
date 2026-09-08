using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Infrastructure.Persistence.Read;

namespace Restaurantes.Orders.Infrastructure.Stores;

public sealed class KitchenOrderReadStore(OrderReadDbContext dbContext) : IKitchenOrderReadStore
{
    public async Task<OrderResponse?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        KitchenOrderReadModel? item = await dbContext
            .KitchenOrders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
        return item is null ? null : Map(item, null);
    }

    public async Task<IReadOnlyList<OrderResponse>> ListAsync(
        Guid restaurantId,
        string? stationCode,
        CancellationToken cancellationToken
    )
    {
        List<KitchenOrderReadModel> items = await dbContext
            .KitchenOrders.AsNoTracking()
            .Where(item =>
                item.RestaurantId == restaurantId
                && (
                    item.Status == "Submitted"
                    || item.Status == "InPreparation"
                    || item.Status == "Ready"
                )
            )
            .OrderBy(item => item.SubmittedAtUtc)
            .ToListAsync(cancellationToken);
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
            lines = lines.Where(line => line.Status != "Cancelled").ToList();
            if (stationCode == "CHEF")
            {
                bool fullyDispatched = stations
                    .Where(station => station.Status != "Cancelled")
                    .All(station => station.Status == "Dispatched");
                if (item.Status == "Ready" && item.ServiceMode != "Takeaway" && fullyDispatched)
                {
                    return null;
                }
            }
            else
            {
                OrderKitchenStationResponse? selected = stations.FirstOrDefault(station =>
                    station.Code == stationCode
                );
                if (selected is null || selected.Status is "Dispatched" or "Cancelled")
                {
                    return null;
                }
                lines = lines.Where(line => line.PreparationStationCode == stationCode).ToList();
                stations = stations.Where(station => station.Code == stationCode).ToList();
            }
            if (lines.Count == 0)
            {
                return null;
            }
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
            stations
        );
    }
}
