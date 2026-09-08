using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Restaurantes.Reporting.Infrastructure.Persistence.Read;

public sealed class ReportingReadDbContext(DbContextOptions<ReportingReadDbContext> options)
    : DbContext(options)
{
    public DbSet<OrderReportingFact> Orders => Set<OrderReportingFact>();
    public DbSet<CompletedSaleFact> Sales => Set<CompletedSaleFact>();
    public DbSet<ReportingInboxMessage> InboxMessages => Set<ReportingInboxMessage>();
    public DbSet<ReportingOutboxMessage> OutboxMessages => Set<ReportingOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        EntityTypeBuilder<OrderReportingFact> o = b.Entity<OrderReportingFact>();
        o.ToTable("order_reporting_facts");
        o.HasKey(x => x.OrderId);
        o.Property(x => x.TableLabel).HasMaxLength(80).IsRequired();
        o.Property(x => x.CustomerName).HasMaxLength(80).IsRequired();
        o.Property(x => x.ServiceMode).HasMaxLength(30).HasDefaultValue("DineIn").IsRequired();
        o.Property(x => x.Source).HasMaxLength(30).IsRequired();
        o.Property(x => x.PaymentTiming).HasMaxLength(30).IsRequired();
        o.Property(x => x.OrderStatus).HasMaxLength(30).IsRequired();
        o.Property(x => x.PaymentStatus).HasMaxLength(30).IsRequired();
        o.Property(x => x.PaymentMethod).HasMaxLength(30);
        o.Property(x => x.Total).HasPrecision(12, 2);
        o.Property(x => x.LinesJson).HasColumnType("jsonb").IsRequired();
        o.Property(x => x.StationsJson).HasColumnType("jsonb").IsRequired();
        o.HasIndex(x => new
        {
            x.RestaurantId,
            x.OrderStatus,
            x.UpdatedAtUtc,
        });
        o.HasIndex(x => x.SaleRecognizedAtUtc);
        o.HasIndex(x => x.PaymentTransactionId);
        EntityTypeBuilder<CompletedSaleFact> s = b.Entity<CompletedSaleFact>();
        s.ToTable("completed_sale_facts");
        s.HasKey(x => x.SaleId);
        s.Property(x => x.Source).HasMaxLength(30).IsRequired();
        s.Property(x => x.PaymentMethod).HasMaxLength(30).IsRequired();
        s.Property(x => x.Total).HasPrecision(12, 2);
        s.Property(x => x.OrderIdsJson).HasColumnType("jsonb").IsRequired();
        s.Property(x => x.LinesJson).HasColumnType("jsonb").IsRequired();
        s.HasIndex(x => new { x.RestaurantId, x.CompletedAtUtc });
        EntityTypeBuilder<ReportingInboxMessage> i = b.Entity<ReportingInboxMessage>();
        i.ToTable("inbox_messages");
        i.HasKey(x => x.Id);
        EntityTypeBuilder<ReportingOutboxMessage> x = b.Entity<ReportingOutboxMessage>();
        x.ToTable("outbox_messages");
        x.HasKey(m => m.Id);
        x.Property(m => m.Type).HasMaxLength(300).IsRequired();
        x.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        x.Property(m => m.Error).HasMaxLength(2000);
        x.HasIndex(m => new { m.ProcessedAtUtc, m.OccurredAtUtc });
    }
}
