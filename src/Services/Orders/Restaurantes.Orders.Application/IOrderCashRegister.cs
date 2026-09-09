namespace Restaurantes.Orders.Application;

public interface IOrderCashRegister
{
    Task EnsureOpenAsync(Guid restaurantId, CancellationToken cancellationToken);
}
