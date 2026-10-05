namespace Restaurantes.Orders.Contracts.Responses;

public sealed record CashCloseOrderResponse(Guid OrderId, bool IsCancelled);
