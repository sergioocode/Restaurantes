namespace Restaurantes.Orders.Application;

public interface IOrderPaymentStore
{
    Task<bool> IsPaidAsync(Guid orderId, CancellationToken ct);
    Task<bool> IsRefundedAsync(Guid orderId, CancellationToken ct);
}
