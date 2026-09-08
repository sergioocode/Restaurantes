using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Restaurantes.Catalog.Domain;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write;

public sealed class CatalogWriteDbContext(DbContextOptions<CatalogWriteDbContext> options)
    : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<RestaurantMenuItem> MenuItems => Set<RestaurantMenuItem>();
    public DbSet<RestaurantKitchenStation> KitchenStations => Set<RestaurantKitchenStation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        EntityTypeBuilder<Category> c = b.Entity<Category>();
        c.ToTable("categories");
        c.HasKey(x => x.Id);
        c.Property(x => x.Code).HasMaxLength(40).IsRequired();
        c.HasIndex(x => x.Code).IsUnique();
        c.Property(x => x.Name).HasMaxLength(120).IsRequired();
        c.Property(x => x.DefaultStationCode).HasMaxLength(40).IsRequired();
        c.Property(x => x.DefaultStationName).HasMaxLength(80).IsRequired();
        c.Property(x => x.Version).IsConcurrencyToken();
        EntityTypeBuilder<Product> p = b.Entity<Product>();
        p.ToTable("products");
        p.HasKey(x => x.Id);
        p.Property(x => x.Sku).HasMaxLength(60).IsRequired();
        p.HasIndex(x => x.Sku).IsUnique();
        p.Property(x => x.Name).HasMaxLength(160).IsRequired();
        p.Property(x => x.BasePrice).HasPrecision(12, 2);
        p.Property(x => x.Version).IsConcurrencyToken();
        p.HasOne<Category>()
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        EntityTypeBuilder<RestaurantMenuItem> m = b.Entity<RestaurantMenuItem>();
        m.ToTable("restaurant_menu_items");
        m.HasKey(x => x.Id);
        m.Property(x => x.Price).HasPrecision(12, 2);
        m.Property(x => x.PreparationStationCode).HasMaxLength(40).IsRequired();
        m.Property(x => x.PreparationStationName).HasMaxLength(80).IsRequired();
        m.Property(x => x.Version).IsConcurrencyToken();
        m.HasIndex(x => new { x.RestaurantId, x.ProductId }).IsUnique();
        m.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        EntityTypeBuilder<RestaurantKitchenStation> s = b.Entity<RestaurantKitchenStation>();
        s.ToTable("restaurant_kitchen_stations");
        s.HasKey(x => x.Id);
        s.Property(x => x.Code).HasMaxLength(40).IsRequired();
        s.Property(x => x.Name).HasMaxLength(80).IsRequired();
        s.Property(x => x.Version).IsConcurrencyToken();
        s.HasIndex(x => new { x.RestaurantId, x.Code }).IsUnique();
        s.HasIndex(x => x.RestaurantId)
            .HasFilter("\"IsPrimary\" = TRUE AND \"IsActive\" = TRUE")
            .IsUnique();
        EntityTypeBuilder<OutboxMessage> o = b.Entity<OutboxMessage>();
        o.ToTable("outbox_messages");
        o.HasKey(x => x.Id);
        o.Property(x => x.Type).HasMaxLength(300).IsRequired();
        o.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        o.Property(x => x.Error).HasMaxLength(2000);
        o.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
    }
}
