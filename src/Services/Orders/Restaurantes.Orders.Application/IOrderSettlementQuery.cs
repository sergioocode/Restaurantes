using Restaurantes.Orders.Contracts.Responses;

namespace Restaurantes.Orders.Application;

public interface IOrderSettlementQuery
{
    Task<IReadOnlyList<CashCloseOrderResponse>> ListAsync(
        Guid restaurantId,
        int offset,
        CancellationToken ct
    );
}
