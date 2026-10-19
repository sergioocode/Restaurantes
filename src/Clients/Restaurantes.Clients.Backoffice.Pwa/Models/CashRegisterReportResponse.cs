namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed record CashRegisterReportResponse(
    Guid Id,
    Guid RestaurantId,
    DateOnly BusinessDate,
    string Status,
    decimal OpeningFloat,
    decimal TotalSales,
    decimal CashSales,
    decimal OtherIncomeTotal,
    decimal OtherExpenseTotal,
    decimal ExpectedCash,
    decimal ExpectedTotal,
    decimal? ReconciledTotal,
    decimal? CountedCash,
    decimal? Difference,
    DateTime OpenedAtUtc,
    string OpenedByName,
    DateTime? ClosedAtUtc,
    string? ClosedByName,
    IReadOnlyCollection<CashRegisterPaymentMethodTotal> PaymentMethods,
    IReadOnlyCollection<CashRegisterPaymentMethodExpected> ExpectedByMethod,
    IReadOnlyCollection<CashRegisterPaymentMethodReconciliation> Reconciliations,
    IReadOnlyCollection<CashMovementReportResponse> Movements
);

public sealed record CashRegisterPaymentMethodTotal(string Method, decimal Sales);

public sealed record CashRegisterPaymentMethodExpected(string Method, decimal Expected);

public sealed record CashRegisterPaymentMethodReconciliation(
    string Method,
    decimal Expected,
    decimal Reconciled,
    decimal Difference
);

public sealed record CashMovementReportResponse(
    Guid Id,
    string Type,
    string Method,
    decimal Amount,
    string Reference,
    DateTime OccurredAtUtc,
    string Category,
    string RecordedByName,
    Guid? OrderId,
    Guid? PaymentId = null,
    Guid? SaleId = null
);
