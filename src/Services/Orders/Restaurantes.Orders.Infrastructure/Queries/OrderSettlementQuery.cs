using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Infrastructure.Queries;

public sealed class OrderSettlementQuery(OrderWriteDbContext db) : IOrderSettlementQuery
{
    public async Task<IReadOnlyList<CashCloseOrderResponse>> ListAsync(
        Guid restaurantId,
        int offset,
        CancellationToken ct
    )
    {
        return await db
            .Orders.AsNoTracking()
            .Where(order => order.RestaurantId == restaurantId)
            .OrderBy(order => order.Id)
            .Skip(offset)
            .Take(500)
            .Select(order => new CashCloseOrderResponse(
                order.Id,
                order.Status == OrderStatus.Cancelled
            ))
            .ToListAsync(ct);
    }
}
