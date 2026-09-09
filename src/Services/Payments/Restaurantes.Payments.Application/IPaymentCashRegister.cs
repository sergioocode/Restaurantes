namespace Restaurantes.Payments.Application;

public interface IPaymentCashRegister
{
    Task EnsureOpenAsync(Guid restaurantId, CancellationToken cancellationToken);
}
