namespace Restaurantes.Payments.Application;

public sealed class PaymentCashRegisterClosedException(Guid restaurantId)
    : InvalidOperationException(
        $"La caja del local '{restaurantId}' está cerrada. Ábrela antes de registrar pagos."
    );
