using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read;

public sealed class CategoryReadModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefaultStationCode { get; set; } = "";
    public string DefaultStationName { get; set; } = "";
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ProductReadModel
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class MenuItemReadModel
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string ProductName { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string CategoryCode { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public string PreparationStationCode { get; set; } = "";
    public string PreparationStationName { get; set; } = "";
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class KitchenStationReadModel
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool RequiresPrimaryDispatch { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public DateTime ProcessedAtUtc { get; set; }
}

public sealed class CatalogReadDbContext(DbContextOptions<CatalogReadDbContext> options)
    : DbContext(options)
{
    public DbSet<CategoryReadModel> Categories => Set<CategoryReadModel>();
    public DbSet<ProductReadModel> Products => Set<ProductReadModel>();
    public DbSet<MenuItemReadModel> MenuItems => Set<MenuItemReadModel>();
    public DbSet<KitchenStationReadModel> KitchenStations => Set<KitchenStationReadModel>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        EntityTypeBuilder<CategoryReadModel> c = b.Entity<CategoryReadModel>();
        c.ToTable("category_views");
        c.HasKey(x => x.Id);
        c.Property(x => x.Code).HasMaxLength(40).IsRequired();
        c.HasIndex(x => x.Code).IsUnique();
        c.Property(x => x.Name).HasMaxLength(120).IsRequired();
        c.Property(x => x.DefaultStationCode).HasMaxLength(40).IsRequired();
        c.Property(x => x.DefaultStationName).HasMaxLength(80).IsRequired();
        EntityTypeBuilder<ProductReadModel> p = b.Entity<ProductReadModel>();
        p.ToTable("product_views");
        p.HasKey(x => x.Id);
        p.Property(x => x.Sku).HasMaxLength(60).IsRequired();
        p.HasIndex(x => x.Sku).IsUnique();
        p.Property(x => x.Name).HasMaxLength(160).IsRequired();
        p.Property(x => x.BasePrice).HasPrecision(12, 2);
        EntityTypeBuilder<MenuItemReadModel> m = b.Entity<MenuItemReadModel>();
        m.ToTable("menu_item_views");
        m.HasKey(x => x.Id);
        m.Property(x => x.Sku).HasMaxLength(60).IsRequired();
        m.Property(x => x.ProductName).HasMaxLength(160).IsRequired();
        m.Property(x => x.CategoryCode).HasMaxLength(40).IsRequired();
        m.Property(x => x.CategoryName).HasMaxLength(120).IsRequired();
        m.Property(x => x.Price).HasPrecision(12, 2);
        m.Property(x => x.PreparationStationCode).HasMaxLength(40).IsRequired();
        m.Property(x => x.PreparationStationName).HasMaxLength(80).IsRequired();
        m.HasIndex(x => new { x.RestaurantId, x.ProductId }).IsUnique();
        EntityTypeBuilder<KitchenStationReadModel> s = b.Entity<KitchenStationReadModel>();
        s.ToTable("kitchen_station_views");
        s.HasKey(x => x.Id);
        s.Property(x => x.Code).HasMaxLength(40).IsRequired();
        s.Property(x => x.Name).HasMaxLength(80).IsRequired();
        s.HasIndex(x => new { x.RestaurantId, x.Code }).IsUnique();
        s.HasIndex(x => x.RestaurantId)
            .HasFilter("\"IsPrimary\" = TRUE AND \"IsActive\" = TRUE")
            .IsUnique();
        EntityTypeBuilder<InboxMessage> i = b.Entity<InboxMessage>();
        i.ToTable("inbox_messages");
        i.HasKey(x => x.Id);
        i.Property(x => x.Type).HasMaxLength(300).IsRequired();
    }
}
