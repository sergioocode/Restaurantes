namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record SessionBillOrderResponse(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    DateTime AddedAtUtc
);
