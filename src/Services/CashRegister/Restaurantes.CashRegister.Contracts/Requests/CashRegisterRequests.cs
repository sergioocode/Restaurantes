namespace Restaurantes.CashRegister.Contracts.Requests;

public sealed record OpenCashRegisterRequest(decimal OpeningFloat);

public sealed record CloseCashRegisterRequest(Dictionary<string, decimal> ReconciledByMethod);

public sealed record CreateCashMovementRequest(
    string Type,
    string Method,
    decimal Amount,
    string Category,
    string Description
);
