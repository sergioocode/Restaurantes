namespace Restaurantes.Orders.Application;

public sealed class OrderCashRegisterClosedException(Guid restaurantId)
    : InvalidOperationException(
        $"La caja del local '{restaurantId}' está cerrada. Ábrela antes de tomar pedidos."
    );
