namespace Restaurantes.Orders.Application;

public sealed record OrderCashRegisterSession(Guid SessionId, DateOnly BusinessDate);

public interface IOrderCashRegister
{
    Task<DateOnly> GetBusinessDateAsync(Guid restaurantId, CancellationToken cancellationToken);

    Task<OrderCashRegisterSession> EnsureOpenAsync(
        Guid restaurantId,
        CancellationToken cancellationToken
    );
}
