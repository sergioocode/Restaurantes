namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record SessionBillOrderResponse(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    decimal AmountPaid,
    DateTime AddedAtUtc,
    decimal TipPaid = 0,
    string Source = "",
    Guid? CreatedByUserId = null
);
