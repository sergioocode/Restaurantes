namespace Restaurantes.Payments.Contracts.Responses;

public sealed record OrderPaymentStatusResponse(Guid OrderId, string Status);
