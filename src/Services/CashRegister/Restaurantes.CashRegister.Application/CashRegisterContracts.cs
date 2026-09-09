namespace Restaurantes.CashRegister.Application;

public sealed record OpenCashRegisterRequest(decimal OpeningFloat);

public sealed record CloseCashRegisterRequest(Dictionary<string, decimal> ReconciledByMethod);

public sealed record CashMovementResponse(
    Guid Id,
    string Type,
    string Method,
    decimal Amount,
    string Reference,
    DateTime OccurredAtUtc
);

public sealed record PaymentMethodTotal(string Method, decimal Sales, decimal Refunds, decimal Net);

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

public sealed record CashRegisterUser(Guid Id, string Name);

public enum CashRegisterOutcome
{
    Ok,
    Created,
    NoContent,
    NotFound,
    Conflict,
    Validation,
}

public sealed record CashRegisterResult(
    CashRegisterOutcome Outcome,
    object? Value = null,
    string? Location = null,
    IReadOnlyDictionary<string, string[]>? Errors = null
);

public sealed record PaymentProjection(
    Guid MessageId,
    Guid PaymentId,
    Guid OrderId,
    Guid RestaurantId,
    decimal Amount,
    string Method,
    string Reference,
    DateTime OccurredAtUtc,
    bool IsRefund
);
