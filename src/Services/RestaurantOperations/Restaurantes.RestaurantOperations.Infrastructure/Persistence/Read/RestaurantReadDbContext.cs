using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;

public sealed class RestaurantReadDbContext(DbContextOptions<RestaurantReadDbContext> options)
    : DbContext(options)
{
    public DbSet<RestaurantReadModel> Restaurants => Set<RestaurantReadModel>();
    public DbSet<CompanyReadModel> Companies => Set<CompanyReadModel>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<RestaurantReadModel> restaurant =
            modelBuilder.Entity<RestaurantReadModel>();
        restaurant.ToTable("restaurant_views");
        restaurant.HasKey(item => item.Id);
        restaurant.Property(item => item.Code).HasMaxLength(40).IsRequired();
        restaurant.HasIndex(item => item.Code).IsUnique();
        restaurant.Property(item => item.Name).HasMaxLength(160).IsRequired();
        restaurant.Property(item => item.Address).HasMaxLength(300).IsRequired();
        restaurant.Property(item => item.CompanyId);

        EntityTypeBuilder<CompanyReadModel> company = modelBuilder.Entity<CompanyReadModel>();
        company.ToTable("company_views");
        company.HasKey(item => item.Id);
        company.Property(item => item.LegalName).HasMaxLength(160).IsRequired();
        company.Property(item => item.TaxId).HasMaxLength(20).IsRequired();
        company.HasIndex(item => item.TaxId).IsUnique();
        company.Property(item => item.FiscalAddress).HasMaxLength(300).IsRequired();
        company.Property(item => item.Phone).HasMaxLength(40);
        company.Property(item => item.Email).HasMaxLength(254);

        EntityTypeBuilder<InboxMessage> inbox = modelBuilder.Entity<InboxMessage>();
        inbox.ToTable("inbox_messages");
        inbox.HasKey(item => item.Id);
        inbox.Property(item => item.Type).HasMaxLength(300).IsRequired();
        inbox.HasIndex(item => item.ProcessedAtUtc);
    }
}
