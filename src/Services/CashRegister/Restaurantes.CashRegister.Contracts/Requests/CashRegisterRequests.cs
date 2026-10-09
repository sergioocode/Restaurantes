namespace Restaurantes.CashRegister.Contracts.Requests;

public sealed record OpenCashRegisterRequest(decimal OpeningFloat);

public sealed record CloseCashRegisterRequest(Dictionary<string, decimal> ReconciledByMethod);
