using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Restaurantes.Payments.Domain;

namespace Restaurantes.Payments.Infrastructure.Persistence.Write;

public sealed class PaymentWriteDbContext(DbContextOptions<PaymentWriteDbContext> options)
    : DbContext(options)
{
    public DbSet<PayableOrder> PayableOrders => Set<PayableOrder>();
    public DbSet<PayableOrderCapture> Captures => Set<PayableOrderCapture>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<PayableOrder> payableOrder = modelBuilder.Entity<PayableOrder>();
        payableOrder.ToTable("payable_orders");
        payableOrder.HasKey(item => item.OrderId);
        payableOrder.HasIndex(item => item.DiningSessionId);
        payableOrder
            .Property(item => item.ServiceMode)
            .HasMaxLength(30)
            .HasDefaultValue("DineIn")
            .IsRequired();
        payableOrder.Property(item => item.Source).HasMaxLength(30).IsRequired();
        payableOrder.Property(item => item.Amount).HasPrecision(12, 2);
        payableOrder.Property(item => item.AmountPaid).HasPrecision(12, 2);
        payableOrder.Property(item => item.TipAmountPaid).HasPrecision(12, 2);
        payableOrder.Property(item => item.Method).HasMaxLength(30);
        payableOrder.Property(item => item.ExternalReference).HasMaxLength(160);
        payableOrder.HasIndex(item => item.CaptureIdempotencyKey);
        payableOrder.Property(item => item.Status).HasConversion<int>();
        payableOrder.Property(item => item.Version).IsConcurrencyToken();
        payableOrder.HasIndex(item => new { item.RestaurantId, item.Status });
        payableOrder
            .HasMany(item => item.Captures)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        EntityTypeBuilder<PayableOrderCapture> capture = modelBuilder.Entity<PayableOrderCapture>();
        capture.ToTable("payment_captures");
        capture.HasKey(item => item.PaymentId);
        capture.HasIndex(item => new { item.OrderId, item.IdempotencyKey }).IsUnique();
        capture.Property(item => item.Amount).HasPrecision(12, 2);
        capture.Property(item => item.TipAmount).HasPrecision(12, 2);
        capture.Property(item => item.Method).HasMaxLength(30).IsRequired();
        capture.Property(item => item.ExternalReference).HasMaxLength(160);

        EntityTypeBuilder<OutboxMessage> outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(item => item.Id);
        outbox.Property(item => item.Type).HasMaxLength(300).IsRequired();
        outbox.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
        outbox.Property(item => item.Error).HasMaxLength(2000);
        outbox.HasIndex(item => new { item.ProcessedAtUtc, item.OccurredAtUtc });

        EntityTypeBuilder<InboxMessage> inbox = modelBuilder.Entity<InboxMessage>();
        inbox.ToTable("inbox_messages");
        inbox.HasKey(item => item.Id);
    }
}
