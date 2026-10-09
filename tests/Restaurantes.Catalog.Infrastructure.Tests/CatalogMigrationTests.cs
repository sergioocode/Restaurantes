using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        string script = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE catalog_menu_prices", script);
        Assert.Contains("ALTER TABLE products DROP COLUMN \"BasePrice\"", script);
        Assert.Contains("ALTER TABLE products ADD COLUMN \"TaxId\"", script);
        Assert.Contains("ADD COLUMN \"AllowsExtras\" boolean", script);
    }

    [Fact]
    public void Read_migration_removes_base_price()
    {
        DbContextOptions<CatalogReadDbContext> options =
            new DbContextOptionsBuilder<CatalogReadDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options;
        using CatalogReadDbContext context = new(options);
        string script = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("DROP COLUMN \"BasePrice\"", script);
    }
}
