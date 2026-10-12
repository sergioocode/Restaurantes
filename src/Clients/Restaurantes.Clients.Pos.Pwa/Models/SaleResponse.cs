namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record SalePaymentBreakdown(string Method, int Payments, decimal Amount);

public sealed record SaleResponse(IReadOnlyList<SalePaymentBreakdown>? PaymentMethods = null);
