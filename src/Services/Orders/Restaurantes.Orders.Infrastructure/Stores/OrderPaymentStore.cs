using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Infrastructure.Stores;

public sealed class OrderPaymentStore(OrderWriteDbContext dbContext) : IOrderPaymentStore
{
    public Task<bool> IsPaidAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return dbContext.OrderPayments.AnyAsync(
            item => item.OrderId == orderId && item.Status == "Paid",
            cancellationToken
        );
    }

    public Task<bool> IsRefundedAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return dbContext.OrderPayments.AnyAsync(
            item => item.OrderId == orderId && item.Status == "Refunded",
            cancellationToken
        );
    }
}
