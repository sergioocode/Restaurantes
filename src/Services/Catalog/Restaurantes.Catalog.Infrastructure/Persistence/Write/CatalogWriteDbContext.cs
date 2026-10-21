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
    public DbSet<CatalogTax> Taxes => Set<CatalogTax>();
    public DbSet<CatalogAllergen> Allergens => Set<CatalogAllergen>();
    public DbSet<ProductAllergen> ProductAllergens => Set<ProductAllergen>();
    public DbSet<ProductPhoto> ProductPhotos => Set<ProductPhoto>();
    public DbSet<ProductConfiguration> ProductConfigurations => Set<ProductConfiguration>();
    public DbSet<ProductOptionGroup> ProductOptionGroups => Set<ProductOptionGroup>();
    public DbSet<ProductOption> ProductOptions => Set<ProductOption>();
    public DbSet<CatalogPrice> Prices => Set<CatalogPrice>();
    public DbSet<CatalogMenu> CatalogMenus => Set<CatalogMenu>();
    public DbSet<RestaurantProductAvailability> RestaurantProductAvailabilities =>
        Set<RestaurantProductAvailability>();
    public DbSet<CatalogMenuPrice> CatalogMenuPrices => Set<CatalogMenuPrice>();
    public DbSet<CatalogMenuEntry> CatalogMenuEntries => Set<CatalogMenuEntry>();
    public DbSet<CatalogMenuChannel> CatalogMenuChannels => Set<CatalogMenuChannel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Category> c = modelBuilder.Entity<Category>();
        c.ToTable("categories");
        c.HasKey(x => x.Id);
        c.Property(x => x.Code).HasMaxLength(40).IsRequired();
        c.HasIndex(x => x.Code).IsUnique();
        c.Property(x => x.Name).HasMaxLength(120).IsRequired();
        c.Property(x => x.DefaultStationCode).HasMaxLength(40).IsRequired();
        c.Property(x => x.DefaultStationName).HasMaxLength(80).IsRequired();
        c.Property(x => x.RequiresKds).HasDefaultValue(true);
        c.Property(x => x.Version).IsConcurrencyToken();
        EntityTypeBuilder<Product> p = modelBuilder.Entity<Product>();
        p.ToTable("products");
        p.HasKey(x => x.Id);
        p.Property(x => x.Sku).HasMaxLength(60).IsRequired();
        p.HasIndex(x => x.Sku).IsUnique();
        p.Property(x => x.Name).HasMaxLength(160).IsRequired();
        p.Property(x => x.Version).IsConcurrencyToken();
        p.HasOne<Category>()
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        p.HasOne<CatalogTax>()
            .WithMany()
            .HasForeignKey(x => x.TaxId)
            .OnDelete(DeleteBehavior.Restrict);
        EntityTypeBuilder<RestaurantMenuItem> m = modelBuilder.Entity<RestaurantMenuItem>();
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
        EntityTypeBuilder<RestaurantKitchenStation> s =
            modelBuilder.Entity<RestaurantKitchenStation>();
        s.ToTable("restaurant_kitchen_stations");
        s.HasKey(x => x.Id);
        s.Property(x => x.Code).HasMaxLength(40).IsRequired();
        s.Property(x => x.Name).HasMaxLength(80).IsRequired();
        s.Property(x => x.PrintTicket).HasDefaultValue(false);
        s.Property(x => x.ManagesZones).HasDefaultValue(false);
        s.Property(x => x.Version).IsConcurrencyToken();
        s.HasIndex(x => new { x.RestaurantId, x.Code }).IsUnique();
        s.HasIndex(x => x.RestaurantId)
            .HasFilter("\"IsPrimary\" = TRUE AND \"IsActive\" = TRUE")
            .IsUnique();
        EntityTypeBuilder<OutboxMessage> o = modelBuilder.Entity<OutboxMessage>();
        o.ToTable("outbox_messages");
        o.HasKey(x => x.Id);
        o.Property(x => x.Type).HasMaxLength(300).IsRequired();
        o.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        o.Property(x => x.Error).HasMaxLength(2000);
        o.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });

        modelBuilder.Entity<CatalogTax>(x =>
        {
            x.ToTable("taxes");
            x.HasKey(t => t.Id);
            x.Property(t => t.Code).HasMaxLength(40).IsRequired();
            x.HasIndex(t => t.Code).IsUnique();
            x.Property(t => t.Name).HasMaxLength(120).IsRequired();
            x.Property(t => t.Rate).HasPrecision(7, 4);
        });
        modelBuilder.Entity<CatalogAllergen>(x =>
        {
            x.ToTable("allergens");
            x.HasKey(a => a.Id);
            x.Property(a => a.Code).HasMaxLength(40).IsRequired();
            x.HasIndex(a => a.Code).IsUnique();
            x.Property(a => a.Name).HasMaxLength(120).IsRequired();
            x.Property(a => a.Description).HasMaxLength(500).IsRequired();
        });
        modelBuilder.Entity<ProductAllergen>(x =>
        {
            x.ToTable("product_allergens");
            x.HasKey(a => new { a.ProductId, a.AllergenId });
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(a => a.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            x.HasOne<CatalogAllergen>()
                .WithMany()
                .HasForeignKey(a => a.AllergenId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProductPhoto>(x =>
        {
            x.ToTable("product_photos");
            x.HasKey(p => p.ProductId);
            x.Property(p => p.OriginalContentType).HasMaxLength(40).IsRequired();
            x.HasOne<Product>()
                .WithOne()
                .HasForeignKey<ProductPhoto>(p => p.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProductConfiguration>(x =>
        {
            x.ToTable("product_configurations");
            x.HasKey(p => p.ProductId);
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(p => p.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProductOptionGroup>(x =>
        {
            x.ToTable("product_option_groups");
            x.HasKey(g => g.Id);
            x.Property(g => g.Name).HasMaxLength(120).IsRequired();
            x.Property(g => g.Kind).HasMaxLength(20).IsRequired();
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(g => g.ParentProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProductOption>(x =>
        {
            x.ToTable("product_options");
            x.HasKey(o => new { o.GroupId, o.ProductId });
            x.HasOne<ProductOptionGroup>()
                .WithMany()
                .HasForeignKey(o => o.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(o => o.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CatalogPrice>(x =>
        {
            x.ToTable("product_prices");
            x.HasKey(p => p.Id);
            x.Property(p => p.Channel).HasMaxLength(20).IsRequired();
            x.Property(p => p.Format).HasMaxLength(20).IsRequired();
            x.Property(p => p.Amount).HasPrecision(12, 2);
            x.HasIndex(p => new
                {
                    p.ProductId,
                    p.RestaurantId,
                    p.Channel,
                    p.Format,
                })
                .IsUnique();
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(p => p.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CatalogMenu>(x =>
        {
            x.ToTable("catalog_menus");
            x.HasKey(m => m.Id);
            x.HasIndex(m => m.RestaurantId).IsUnique();
            x.ToTable(t =>
                t.HasCheckConstraint(
                    "CK_catalog_menus_global_only",
                    "\"RestaurantId\" = '00000000-0000-0000-0000-000000000000'"
                )
            );
            x.Property(m => m.Name).HasMaxLength(120).IsRequired();
        });
        modelBuilder.Entity<RestaurantProductAvailability>(x =>
        {
            x.ToTable("restaurant_product_availability");
            x.HasKey(a => new { a.RestaurantId, a.ProductId });
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(a => a.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CatalogMenuEntry>(x =>
        {
            x.ToTable("catalog_menu_entries");
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.MenuId, e.ProductId }).IsUnique();
            x.Property(e => e.DefaultFormat).HasMaxLength(20).IsRequired();
            x.HasOne<CatalogMenu>()
                .WithMany()
                .HasForeignKey(e => e.MenuId)
                .OnDelete(DeleteBehavior.Cascade);
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CatalogMenuPrice>(x =>
        {
            x.ToTable("catalog_menu_prices");
            x.HasKey(p => new
            {
                p.MenuId,
                p.ProductId,
                p.Channel,
                p.Format,
            });
            x.Property(p => p.Channel).HasMaxLength(20).IsRequired();
            x.Property(p => p.Format).HasMaxLength(20).IsRequired();
            x.Property(p => p.Amount).HasPrecision(12, 2);
            x.HasOne<CatalogMenu>()
                .WithMany()
                .HasForeignKey(p => p.MenuId)
                .OnDelete(DeleteBehavior.Cascade);
            x.HasOne<Product>()
                .WithMany()
                .HasForeignKey(p => p.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CatalogMenuChannel>(x =>
        {
            x.ToTable("catalog_menu_channels");
            x.HasKey(c => new { c.MenuEntryId, c.Channel });
            x.Property(c => c.Channel).HasMaxLength(20).IsRequired();
            x.Property(c => c.ExternalId).HasMaxLength(160).IsRequired();
            x.HasOne<CatalogMenuEntry>()
                .WithMany()
                .HasForeignKey(c => c.MenuEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
