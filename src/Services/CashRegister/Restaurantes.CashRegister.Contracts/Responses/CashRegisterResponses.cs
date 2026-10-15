namespace Restaurantes.CashRegister.Contracts.Responses;

public sealed record CashMovementResponse(
    Guid Id,
    string Type,
    string Method,
    decimal Amount,
    string Reference,
    DateTime OccurredAtUtc
);

public sealed record PaymentMethodTotal(string Method, decimal Sales);

public sealed record PaymentMethodExpected(string Method, decimal Expected);

public sealed record PaymentMethodReconciliation(
    string Method,
    decimal Expected,
    decimal Reconciled,
    decimal Difference
);

public sealed record CashRegisterResponse(
    Guid Id,
    Guid RestaurantId,
    DateOnly BusinessDate,
    string Status,
    decimal OpeningFloat,
    decimal TotalSales,
    decimal CashSales,
    decimal ExpectedCash,
    decimal ExpectedTotal,
    decimal? ReconciledTotal,
    decimal? CountedCash,
    decimal? Difference,
    DateTime OpenedAtUtc,
    string OpenedByName,
    DateTime? ClosedAtUtc,
    string? ClosedByName,
    IReadOnlyCollection<PaymentMethodTotal> PaymentMethods,
    IReadOnlyCollection<PaymentMethodExpected> ExpectedByMethod,
    IReadOnlyCollection<PaymentMethodReconciliation> Reconciliations,
    IReadOnlyCollection<CashMovementResponse> Movements
);

public sealed record CashRegisterAvailabilityResponse(
    Guid RestaurantId,
    bool IsOpen,
    DateOnly BusinessDate,
    Guid? SessionId
);
