namespace Restaurantes.CashRegister.Domain;

public enum CashMovementType
{
    Sale,
    CardTender,
    CashChange,
    Income,
    Expense,
}

public sealed class CashMovement
{
    public Guid Id { get; set; }
    public Guid CashRegisterSessionId { get; set; }
    public CashRegisterSession Session { get; set; } = null!;
    public Guid? PaymentId { get; set; }
    public Guid? SaleId { get; set; }
    public Guid? OrderId { get; set; }
    public CashMovementType Type { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
