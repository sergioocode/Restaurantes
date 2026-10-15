namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record SalePaymentBreakdown(string Method, int Payments, decimal Amount);

public sealed record SaleResponse(
    string PaymentMethod = "",
    DateTime? CompletedAtUtc = null,
    decimal TipTotal = 0,
    IReadOnlyList<SalePaymentBreakdown>? PaymentMethods = null,
    string? DiscountName = null,
    decimal DiscountPercentage = 0,
    decimal DiscountAmount = 0
);
