namespace Restaurantes.Dining.Application;

public interface IDiningCashRegister
{
    Task EnsureOpenAsync(Guid restaurantId, CancellationToken ct);
}

public sealed class DiningCashRegisterClosedException(string message) : Exception(message) { }
