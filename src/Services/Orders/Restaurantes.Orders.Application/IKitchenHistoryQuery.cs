using Restaurantes.Orders.Contracts.Responses;

namespace Restaurantes.Orders.Application;

public interface IKitchenHistoryQuery
{
    Task<IReadOnlyList<KitchenHistoryResponse>?> ListAsync(
        Guid restaurantId,
        DateOnly businessDate,
        string stationCode,
        CancellationToken ct,
        Guid? zoneId = null
    );
}
