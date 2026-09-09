namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record SessionBillOrderResponse(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    DateTime AddedAtUtc
);
