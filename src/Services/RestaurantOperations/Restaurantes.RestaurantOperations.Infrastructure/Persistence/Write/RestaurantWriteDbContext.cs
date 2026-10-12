using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Restaurantes.RestaurantOperations.Domain;

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;

public sealed class RestaurantWriteDbContext(DbContextOptions<RestaurantWriteDbContext> options)
    : DbContext(options)
{
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Restaurant> restaurant = modelBuilder.Entity<Restaurant>();
        restaurant.ToTable("restaurants");
        restaurant.HasKey(item => item.Id);
        restaurant.Property(item => item.Code).HasMaxLength(40).IsRequired();
        restaurant.HasIndex(item => item.Code).IsUnique();
        restaurant.Property(item => item.Name).HasMaxLength(160).IsRequired();
        restaurant.Property(item => item.Address).HasMaxLength(300).IsRequired();
        restaurant.Property(item => item.Version).IsConcurrencyToken();
        restaurant.Property(item => item.CompanyId);
        restaurant
            .HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        EntityTypeBuilder<Company> company = modelBuilder.Entity<Company>();
        company.ToTable("companies");
        company.HasKey(item => item.Id);
        company.Property(item => item.LegalName).HasMaxLength(160).IsRequired();
        company.Property(item => item.TaxId).HasMaxLength(20).IsRequired();
        company.HasIndex(item => item.TaxId).IsUnique();
        company.Property(item => item.FiscalAddress).HasMaxLength(300).IsRequired();
        company.Property(item => item.Phone).HasMaxLength(40);
        company.Property(item => item.Email).HasMaxLength(254);
        company.Property(item => item.Version).IsConcurrencyToken();

        EntityTypeBuilder<OutboxMessage> outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(item => item.Id);
        outbox.Property(item => item.Type).HasMaxLength(300).IsRequired();
        outbox.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
        outbox.Property(item => item.Error).HasMaxLength(2000);
        outbox.HasIndex(item => new { item.ProcessedAtUtc, item.OccurredAtUtc });
    }
}
