using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Restaurantes.Sales.Domain;

namespace Restaurantes.Sales.Infrastructure.Persistence.Write;

public sealed class SaleWriteDbContext(DbContextOptions<SaleWriteDbContext> options)
    : DbContext(options)
{
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleOrder> SaleOrders => Set<SaleOrder>();
    public DbSet<DeliveredOrderMarker> DeliveredOrders => Set<DeliveredOrderMarker>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Sale> sale = modelBuilder.Entity<Sale>();
        sale.ToTable("sales");
        sale.HasKey(item => item.Id);
        sale.Property(item => item.Source).HasMaxLength(30).IsRequired();
        sale.Property(item => item.PaymentMethod).HasMaxLength(30).IsRequired();
        sale.Property(item => item.Status).HasMaxLength(30).IsRequired();
        sale.Property(item => item.Total).HasPrecision(12, 2);
        sale.HasMany(item => item.Orders)
            .WithOne()
            .HasForeignKey(item => item.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
        sale.HasIndex(item => new { item.RestaurantId, item.CompletedAtUtc });

        EntityTypeBuilder<SaleOrder> order = modelBuilder.Entity<SaleOrder>();
        order.ToTable("sale_orders");
        order.HasKey(item => new { item.SaleId, item.OrderId });
        order.Property(item => item.Amount).HasPrecision(12, 2);
        order.Property(item => item.LinesJson).HasColumnType("jsonb").IsRequired();
        order.HasIndex(item => item.OrderId).IsUnique();

        EntityTypeBuilder<DeliveredOrderMarker> marker =
            modelBuilder.Entity<DeliveredOrderMarker>();
        marker.ToTable("delivered_order_markers");
        marker.HasKey(item => item.OrderId);
        marker.Property(item => item.LinesJson).HasColumnType("jsonb").IsRequired();

        modelBuilder.Entity<InboxMessage>().ToTable("inbox_messages").HasKey(item => item.Id);

        EntityTypeBuilder<OutboxMessage> outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(item => item.Id);
        outbox.Property(item => item.Type).HasMaxLength(300).IsRequired();
        outbox.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
        outbox.Property(item => item.Error).HasMaxLength(2000);
        outbox.HasIndex(item => new { item.ProcessedAtUtc, item.OccurredAtUtc });
    }
}
