using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Restaurantes.CashRegister.Api;

public enum CashRegisterStatus
{
    Open,
    Closed,
    Expired,
}

public enum CashMovementType
{
    Sale,
    Refund,
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

public sealed class CashRegisterInboxMessage
{
    public Guid Id { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

public sealed class CashRegisterDbContext(DbContextOptions<CashRegisterDbContext> options)
    : DbContext(options)
{
    public DbSet<CashRegisterSession> Sessions => Set<CashRegisterSession>();
    public DbSet<CashMovement> Movements => Set<CashMovement>();
    public DbSet<CashRegisterReconciliation> Reconciliations => Set<CashRegisterReconciliation>();
    public DbSet<CashRegisterInboxMessage> InboxMessages => Set<CashRegisterInboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<CashRegisterSession> session = modelBuilder.Entity<CashRegisterSession>();
        session.ToTable("cash_register_sessions");
        session.HasKey(x => x.Id);
        session.HasIndex(x => new { x.RestaurantId, x.BusinessDate });
        session.HasIndex(x => x.RestaurantId).IsUnique().HasFilter("\"Status\" = 0");
        session.Property(x => x.Status).HasConversion<int>();
        session.Property(x => x.OpeningFloat).HasPrecision(12, 2);
        session.Property(x => x.CountedCash).HasPrecision(12, 2);
        session.Property(x => x.ExpectedCashAtClose).HasPrecision(12, 2);
        session.Property(x => x.DifferenceAtClose).HasPrecision(12, 2);
        session.Property(x => x.ExpectedTotalAtClose).HasPrecision(12, 2);
        session.Property(x => x.ReconciledTotalAtClose).HasPrecision(12, 2);
        session.Property(x => x.OpenedByName).HasMaxLength(120).IsRequired();
        session.Property(x => x.ClosedByName).HasMaxLength(120);
        session.Property(x => x.Version).IsConcurrencyToken();
        EntityTypeBuilder<CashMovement> movement = modelBuilder.Entity<CashMovement>();
        movement.ToTable("cash_movements");
        movement.HasKey(x => x.Id);
        movement.HasIndex(x => new { x.PaymentId, x.Type }).IsUnique();
        movement.Property(x => x.Type).HasConversion<int>();
        movement.Property(x => x.Amount).HasPrecision(12, 2);
        movement.Property(x => x.Method).HasMaxLength(40).IsRequired();
        movement.Property(x => x.Reference).HasMaxLength(160);
        movement
            .HasOne(x => x.Session)
            .WithMany(x => x.Movements)
            .HasForeignKey(x => x.CashRegisterSessionId);
        EntityTypeBuilder<CashRegisterReconciliation> reconciliation =
            modelBuilder.Entity<CashRegisterReconciliation>();
        reconciliation.ToTable("cash_register_reconciliations");
        reconciliation.HasKey(x => x.Id);
        reconciliation.HasIndex(x => new { x.CashRegisterSessionId, x.Method }).IsUnique();
        reconciliation.Property(x => x.Method).HasMaxLength(40).IsRequired();
        reconciliation.Property(x => x.ExpectedAmount).HasPrecision(12, 2);
        reconciliation.Property(x => x.ReconciledAmount).HasPrecision(12, 2);
        reconciliation.Property(x => x.Difference).HasPrecision(12, 2);
        reconciliation
            .HasOne(x => x.Session)
            .WithMany(x => x.Reconciliations)
            .HasForeignKey(x => x.CashRegisterSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CashRegisterInboxMessage>().ToTable("inbox_messages").HasKey(x => x.Id);
    }
}

public sealed class CashRegisterDbContextFactory
    : IDesignTimeDbContextFactory<CashRegisterDbContext>
{
    public CashRegisterDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<CashRegisterDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=cash_register_write;Username=restaurants;Password=restaurants_dev"
                )
                .Options
        );
    }
}
