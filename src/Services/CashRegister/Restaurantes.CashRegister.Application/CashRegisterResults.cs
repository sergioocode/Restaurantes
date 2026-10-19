namespace Restaurantes.CashRegister.Application;

public sealed record CashRegisterUser(Guid Id, string Name);

public enum CashRegisterOutcome
{
    Ok,
    Created,
    NoContent,
    NotFound,
    Conflict,
    Validation,
    ServiceUnavailable,
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
    Guid? CashRegisterSessionId = null,
    decimal TenderedAmount = 0,
    decimal ChangeAmount = 0,
    Guid? SaleId = null
);
