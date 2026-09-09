namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record DiningSessionOrderResponse(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount
);
