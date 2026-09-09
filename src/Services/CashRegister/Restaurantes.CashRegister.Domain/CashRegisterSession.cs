namespace Restaurantes.CashRegister.Domain;

public enum CashRegisterStatus
{
    Open,
    Closed,
    Expired,
}

public sealed class CashRegisterSession
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public CashRegisterStatus Status { get; set; }
    public decimal OpeningFloat { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public Guid OpenedByUserId { get; set; }
    public string OpenedByName { get; set; } = string.Empty;
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string? ClosedByName { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? ExpectedCashAtClose { get; set; }
    public decimal? DifferenceAtClose { get; set; }
    public decimal? ExpectedTotalAtClose { get; set; }
    public decimal? ReconciledTotalAtClose { get; set; }
    public int Version { get; set; } = 1;
    public List<CashMovement> Movements { get; set; } = [];
    public List<CashRegisterReconciliation> Reconciliations { get; set; } = [];
}
