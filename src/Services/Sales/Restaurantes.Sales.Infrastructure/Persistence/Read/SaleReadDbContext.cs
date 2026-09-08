using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Restaurantes.Sales.Infrastructure.Persistence.Read;

public sealed class SaleReadDbContext(DbContextOptions<SaleReadDbContext> options)
    : DbContext(options)
{
    public DbSet<SaleReadModel> Sales => Set<SaleReadModel>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<SaleReadModel> sale = modelBuilder.Entity<SaleReadModel>();
        sale.ToTable("sales");
        sale.HasKey(item => item.Id);
        sale.Property(item => item.Source).HasMaxLength(30).IsRequired();
        sale.Property(item => item.PaymentMethod).HasMaxLength(30).IsRequired();
        sale.Property(item => item.Status).HasMaxLength(30).IsRequired();
        sale.Property(item => item.Total).HasPrecision(12, 2);
        sale.Property(item => item.OrderIdsJson).HasColumnType("jsonb").IsRequired();
        sale.Property(item => item.LinesJson).HasColumnType("jsonb").IsRequired();
        sale.HasIndex(item => new { item.RestaurantId, item.CompletedAtUtc });

        modelBuilder.Entity<InboxMessage>().ToTable("inbox_messages").HasKey(item => item.Id);
    }
}
