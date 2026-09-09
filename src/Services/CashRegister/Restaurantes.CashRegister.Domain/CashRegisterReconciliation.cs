namespace Restaurantes.CashRegister.Domain;

public sealed class CashRegisterReconciliation
{
    public Guid Id { get; set; }
    public Guid CashRegisterSessionId { get; set; }
    public CashRegisterSession Session { get; set; } = null!;
    public string Method { get; set; } = string.Empty;
    public decimal ExpectedAmount { get; set; }
    public decimal ReconciledAmount { get; set; }
    public decimal Difference { get; set; }
}
