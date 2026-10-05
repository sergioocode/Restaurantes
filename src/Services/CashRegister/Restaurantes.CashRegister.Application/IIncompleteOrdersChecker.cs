namespace Restaurantes.CashRegister.Application;

public interface IIncompleteOrdersChecker
{
    Task<int> CountAsync(Guid restaurantId, string authorization, CancellationToken ct);
}

public sealed class IncompleteOrdersUnavailableException(Exception innerException)
    : Exception("No se pudo verificar el estado de los pedidos.", innerException);
