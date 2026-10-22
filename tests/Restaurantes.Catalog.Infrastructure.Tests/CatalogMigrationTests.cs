using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogMigrationTests
{
    [Fact]
    public void Write_migration_includes_catalog_menu_selection_and_removes_base_price()
    {
        DbContextOptions<CatalogWriteDbContext> options =
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options;
        using CatalogWriteDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();
        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");
        Assert.Contains("CREATE TABLE catalog_menu_prices", script);
        Assert.Contains("ALTER TABLE products DROP COLUMN \"BasePrice\"", script);
        Assert.Contains("ALTER TABLE products ADD COLUMN \"TaxId\"", script);
        Assert.Contains("ADD COLUMN \"AllowsExtras\" boolean", script);
        Assert.DoesNotContain("Vista completa", script);
        Assert.DoesNotContain("INSERT INTO restaurant_kitchen_stations", script);
        Assert.Contains("ADD COLUMN \"BasePrice\"", rollback);
    }

    [Fact]
    public void Read_migration_removes_base_price()
    {
        DbContextOptions<CatalogReadDbContext> options =
            new DbContextOptionsBuilder<CatalogReadDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options;
        using CatalogReadDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();
        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");
        Assert.Contains("DROP COLUMN \"BasePrice\"", script);
        Assert.DoesNotContain("Vista completa", script);
        Assert.DoesNotContain("INSERT INTO kitchen_station_views", script);
        Assert.Contains("DROP COLUMN \"ManagesZones\"", rollback);
    }

    private static void AssertSnapshotMatchesCurrentModel(DbContext context)
    {
        ModelSnapshot snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!;
        IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
        IModel snapshotModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);

        Assert.False(
            differ.HasDifferences(
                snapshotModel.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel()
            )
        );
    }
}
