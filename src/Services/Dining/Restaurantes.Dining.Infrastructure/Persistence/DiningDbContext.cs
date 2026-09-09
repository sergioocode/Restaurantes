using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Infrastructure.Persistence;

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
