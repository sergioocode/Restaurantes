using Microsoft.EntityFrameworkCore;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Responses;
using Restaurantes.Payments.Infrastructure.Persistence.Write;

namespace Restaurantes.Payments.Infrastructure.Queries;

public sealed class PaymentStatusQuery(PaymentWriteDbContext db) : IPaymentStatusQuery
{
    public async Task<IReadOnlyList<OrderPaymentStatusResponse>> ListAsync(
        Guid restaurantId,
        Guid[] orderIds,
        CancellationToken ct
    )
    {
        return await db
            .PayableOrders.AsNoTracking()
            .Where(payment =>
                payment.RestaurantId == restaurantId && orderIds.Contains(payment.OrderId)
            )
            .Select(payment => new OrderPaymentStatusResponse(
                payment.OrderId,
                payment.Status == Restaurantes.Payments.Domain.PaymentStatus.Open
                && payment.AmountPaid > 0
                    ? "PartiallyPaid"
                    : payment.Status.ToString()
            ))
            .ToListAsync(ct);
    }
}
