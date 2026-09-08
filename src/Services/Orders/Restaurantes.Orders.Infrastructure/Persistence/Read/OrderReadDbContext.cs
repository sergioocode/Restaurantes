using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Restaurantes.Orders.Infrastructure.Persistence.Read;

public sealed class OrderReadDbContext(DbContextOptions<OrderReadDbContext> options)
    : DbContext(options)
{
    public DbSet<KitchenOrderReadModel> KitchenOrders => Set<KitchenOrderReadModel>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<KitchenOrderReadModel> order =
            modelBuilder.Entity<KitchenOrderReadModel>();
        order.ToTable("kitchen_order_views");
        order.HasKey(item => item.Id);
        order.Property(item => item.TableLabel).HasMaxLength(80).IsRequired();
        order.Property(item => item.CustomerName).HasMaxLength(80).IsRequired();
        order
            .Property(item => item.ServiceMode)
            .HasMaxLength(30)
            .HasDefaultValue("DineIn")
            .IsRequired();
        order.Property(item => item.Source).HasMaxLength(30).HasDefaultValue("Pos").IsRequired();
        order
            .Property(item => item.PaymentTiming)
            .HasMaxLength(30)
            .HasDefaultValue("OnAccount")
            .IsRequired();
        order.Property(item => item.Total).HasPrecision(12, 2);
        order.Property(item => item.Status).HasMaxLength(40).IsRequired();
        order.Property(item => item.LinesJson).HasColumnType("jsonb").IsRequired();
        order
            .Property(item => item.StationsJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        order.HasIndex(item => new
        {
            item.RestaurantId,
            item.Status,
            item.SubmittedAtUtc,
        });

        EntityTypeBuilder<InboxMessage> inbox = modelBuilder.Entity<InboxMessage>();
        inbox.ToTable("inbox_messages");
        inbox.HasKey(item => item.Id);
        inbox.Property(item => item.Type).HasMaxLength(300).IsRequired();
        inbox.HasIndex(item => item.ProcessedAtUtc);
    }
}
