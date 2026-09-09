namespace Restaurantes.CashRegister.Domain;

public enum CashMovementType
{
    Sale,
    Refund,
}

public sealed class CashMovement
{
    public Guid Id { get; set; }
    public Guid CashRegisterSessionId { get; set; }
    public CashRegisterSession Session { get; set; } = null!;
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public CashMovementType Type { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
