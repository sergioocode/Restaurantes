using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Infrastructure.Persistence.Write;

public sealed class OrderWriteDbContext(DbContextOptions<OrderWriteDbContext> options)
    : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderKitchenStation> OrderKitchenStations => Set<OrderKitchenStation>();
    public DbSet<OrderCatalogItem> CatalogItems => Set<OrderCatalogItem>();
    public DbSet<OrderKitchenStationConfiguration> KitchenStationConfigurations =>
        Set<OrderKitchenStationConfiguration>();
    public DbSet<OrderPaymentProjection> OrderPayments => Set<OrderPaymentProjection>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Order> order = modelBuilder.Entity<Order>();
        order.ToTable("orders");
        order.HasKey(item => item.Id);
        order.Property(item => item.TableLabel).HasMaxLength(80).IsRequired();
        order.Property(item => item.CustomerName).HasMaxLength(80).IsRequired();
        order.Property(item => item.ServiceMode).HasConversion<int>();
        order.Property(item => item.Status).HasConversion<int>();
        order.Property(item => item.Source).HasConversion<int>();
        order.Property(item => item.PaymentTiming).HasConversion<int>();
        order.Property(item => item.Version).IsConcurrencyToken();
        order.HasIndex(item => new
        {
            item.RestaurantId,
            item.Status,
            item.CreatedAtUtc,
        });
        order
            .HasMany(item => item.Lines)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        order.Navigation(item => item.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        order
            .HasMany(item => item.Stations)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        order.Navigation(item => item.Stations).UsePropertyAccessMode(PropertyAccessMode.Field);

        EntityTypeBuilder<OrderLine> line = modelBuilder.Entity<OrderLine>();
        line.ToTable("order_lines");
        line.HasKey(item => item.Id);
        line.Property(item => item.ProductName).HasMaxLength(160).IsRequired();
        line.Property(item => item.UnitPrice).HasPrecision(12, 2);
        line.Property(item => item.Notes).HasMaxLength(500);
        line.Property(item => item.CategoryName)
            .HasMaxLength(120)
            .HasDefaultValue("General")
            .IsRequired();
        line.Property(item => item.PreparationStationCode)
            .HasMaxLength(40)
            .HasDefaultValue("GENERAL")
            .IsRequired();
        line.Property(item => item.PreparationStationName)
            .HasMaxLength(80)
            .HasDefaultValue("General")
            .IsRequired();
        line.Property(item => item.Status).HasConversion<int>();
        line.Property(item => item.CancellationReason).HasMaxLength(300).IsRequired();

        EntityTypeBuilder<OrderKitchenStation> station = modelBuilder.Entity<OrderKitchenStation>();
        station.ToTable("order_kitchen_stations");
        station.HasKey(item => item.Id);
        station.Property(item => item.Code).HasMaxLength(40).IsRequired();
        station.Property(item => item.Name).HasMaxLength(80).IsRequired();
        station.Property(item => item.Status).HasConversion<int>();
        station.HasIndex(item => new { item.OrderId, item.Code }).IsUnique();

        EntityTypeBuilder<OrderCatalogItem> catalog = modelBuilder.Entity<OrderCatalogItem>();
        catalog.ToTable("catalog_items");
        catalog.HasKey(item => item.Id);
        catalog.Property(item => item.ProductName).HasMaxLength(160).IsRequired();
        catalog.Property(item => item.Price).HasPrecision(12, 2);
        catalog.Property(item => item.CategoryName).HasMaxLength(120).IsRequired();
        catalog.Property(item => item.StationCode).HasMaxLength(40).IsRequired();
        catalog.Property(item => item.StationName).HasMaxLength(80).IsRequired();
        catalog.HasIndex(item => new { item.RestaurantId, item.ProductId }).IsUnique();

        EntityTypeBuilder<OrderKitchenStationConfiguration> stationConfiguration =
            modelBuilder.Entity<OrderKitchenStationConfiguration>();
        stationConfiguration.ToTable("kitchen_station_configurations");
        stationConfiguration.HasKey(item => item.Id);
        stationConfiguration.Property(item => item.Code).HasMaxLength(40).IsRequired();
        stationConfiguration.Property(item => item.Name).HasMaxLength(80).IsRequired();
        stationConfiguration.HasIndex(item => new { item.RestaurantId, item.Code }).IsUnique();

        EntityTypeBuilder<OrderPaymentProjection> payment =
            modelBuilder.Entity<OrderPaymentProjection>();
        payment.ToTable("order_payments");
        payment.HasKey(item => item.OrderId);
        payment.Property(item => item.Status).HasMaxLength(20).IsRequired();
        payment.Property(item => item.Amount).HasPrecision(12, 2);

        EntityTypeBuilder<OutboxMessage> outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(item => item.Id);
        outbox.Property(item => item.Type).HasMaxLength(300).IsRequired();
        outbox.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
        outbox.Property(item => item.Error).HasMaxLength(2000);
        outbox.HasIndex(item => new { item.ProcessedAtUtc, item.OccurredAtUtc });
    }
}
