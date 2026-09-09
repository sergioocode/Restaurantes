namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record CashRegisterResponse(
    Guid Id,
    Guid RestaurantId,
    DateOnly BusinessDate,
    string Status,
    decimal OpeningFloat,
    decimal TotalSales,
    decimal TotalRefunds,
    decimal CashSales,
    decimal CashRefunds,
    decimal ExpectedCash,
    decimal ExpectedTotal,
    decimal? ReconciledTotal,
    decimal? CountedCash,
    decimal? Difference,
    DateTime OpenedAtUtc,
    string OpenedByName,
    DateTime? ClosedAtUtc,
    string? ClosedByName,
    List<PaymentMethodTotal> PaymentMethods,
    List<PaymentMethodExpected> ExpectedByMethod,
    List<PaymentMethodReconciliation> Reconciliations,
    List<CashMovementResponse> Movements
);
