using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Restaurantes.Dining.Api.Write;

public sealed class DiningDbContext(DbContextOptions<DiningDbContext> options) : DbContext(options)
{
    public DbSet<DiningZone> Zones => Set<DiningZone>();
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<DiningRestaurantPolicy> RestaurantPolicies => Set<DiningRestaurantPolicy>();
    public DbSet<DiningSession> Sessions => Set<DiningSession>();
    public DbSet<DiningSessionOrder> SessionOrders => Set<DiningSessionOrder>();
    public DbSet<DiningPendingPayment> PendingPayments => Set<DiningPendingPayment>();
    public DbSet<DiningInboxMessage> InboxMessages => Set<DiningInboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<DiningZone> zone = modelBuilder.Entity<DiningZone>();
        zone.ToTable("dining_zones");
        zone.HasKey(x => x.Id);
        zone.HasAlternateKey(x => new { x.RestaurantId, x.Id });
        zone.Property(x => x.Name).HasMaxLength(80).IsRequired();
        zone.Property(x => x.DeletedAtUtc).IsConcurrencyToken();
        zone.HasIndex(x => new { x.RestaurantId, x.Name })
            .IsUnique()
            .HasFilter("\"DeletedAtUtc\" IS NULL");

        EntityTypeBuilder<RestaurantTable> table = modelBuilder.Entity<RestaurantTable>();
        table.ToTable("restaurant_tables");
        table.HasKey(x => x.Id);
        table.Property(x => x.Code).HasMaxLength(40).IsRequired();
        table.Property(x => x.Label).HasMaxLength(80).IsRequired();
        table.Property(x => x.QrCode).HasMaxLength(64).IsRequired();
        table
            .HasIndex(x => new { x.RestaurantId, x.Code })
            .IsUnique()
            .HasFilter("\"DeletedAtUtc\" IS NULL");
        table.HasIndex(x => x.QrCode).IsUnique();
        table
            .HasOne(x => x.Zone)
            .WithMany()
            .HasForeignKey(x => new { x.RestaurantId, x.ZoneId })
            .HasPrincipalKey(x => new { x.RestaurantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        EntityTypeBuilder<DiningRestaurantPolicy> policy =
            modelBuilder.Entity<DiningRestaurantPolicy>();
        policy.ToTable("dining_restaurant_policies");
        policy.HasKey(x => x.RestaurantId);
        policy.Property(x => x.QrAllowedNetworks).HasMaxLength(2000).IsRequired();
        policy.Property(x => x.QrRequiresImmediatePayment).HasDefaultValue(true);
        policy.Property(x => x.TakeawayRequiresPrepayment).HasDefaultValue(true);
        policy.Property(x => x.AllowCheckoutBeforeKitchenCompletion).HasDefaultValue(false);
        policy.Property(x => x.Version).IsConcurrencyToken();

        EntityTypeBuilder<DiningSession> session = modelBuilder.Entity<DiningSession>();
        session.ToTable("dining_sessions");
        session.HasKey(x => x.Id);
        session.Property(x => x.Source).HasMaxLength(30).IsRequired();
        session.Property(x => x.Status).HasMaxLength(20).IsRequired();
        session.Property(x => x.PaymentMethod).HasMaxLength(30);
        session.Property(x => x.CustomerAccessToken).HasMaxLength(64).IsRequired();
        session.Property(x => x.CancellationReason).HasMaxLength(200).IsRequired();
        session.Property(x => x.Version).IsConcurrencyToken();
        session
            .HasIndex(x => new { x.TableId, x.Status })
            .IsUnique()
            .HasFilter("\"Status\" = 'Open'");
        session
            .HasOne<RestaurantTable>()
            .WithMany()
            .HasForeignKey(x => x.TableId)
            .OnDelete(DeleteBehavior.Restrict);

        EntityTypeBuilder<DiningSessionOrder> order = modelBuilder.Entity<DiningSessionOrder>();
        order.ToTable("dining_session_orders");
        order.HasKey(x => x.OrderId);
        order.Property(x => x.PaymentStatus).HasMaxLength(20).IsRequired();
        order.Property(x => x.OrderStatus).HasMaxLength(30).IsRequired();
        order.Property(x => x.Amount).HasPrecision(12, 2);
        order.HasIndex(x => x.DiningSessionId);
        order
            .HasOne<DiningSession>()
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.DiningSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        EntityTypeBuilder<DiningPendingPayment> pending =
            modelBuilder.Entity<DiningPendingPayment>();
        pending.ToTable("pending_payments");
        pending.HasKey(x => x.OrderId);
        pending.Property(x => x.Status).HasMaxLength(20).IsRequired();

        EntityTypeBuilder<DiningInboxMessage> inbox = modelBuilder.Entity<DiningInboxMessage>();
        inbox.ToTable("inbox_messages");
        inbox.HasKey(x => x.Id);
    }
}

public sealed class RestaurantTable
{
    public bool RequestGuestCount { get; set; }
    public Guid ZoneId { get; set; }
    public DiningZone Zone { get; set; } = null!;
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string QrCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class DiningZone
{
    public DateTime? DeletedAtUtc { get; set; }
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class DiningRestaurantPolicy
{
    public Guid RestaurantId { get; set; }
    public bool QrRequiresImmediatePayment { get; set; } = true;
    public bool RequireTrustedNetworkForQr { get; set; }
    public bool TakeawayRequiresPrepayment { get; set; } = true;
    public bool AllowCheckoutBeforeKitchenCompletion { get; set; }
    public string QrAllowedNetworks { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class DiningSession
{
    public bool RequestGuestCount { get; set; }
    public int? GuestCount { get; set; }
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public int Version { get; set; } = 1;
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? CheckoutIdempotencyKey { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string CustomerAccessToken { get; set; } = string.Empty;
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public List<DiningSessionOrder> Orders { get; set; } = [];
}

public sealed class DiningSessionOrder
{
    public Guid OrderId { get; set; }
    public Guid DiningSessionId { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid";
    public string OrderStatus { get; set; } = "Draft";
    public decimal Amount { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DiningPendingPayment
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = "Unpaid";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DiningInboxMessage
{
    public Guid Id { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
