using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleWriteMigrationTests
{
    [Fact]
    public void Write_migrations_preserve_sales_payment_tips_and_discount_snapshots()
    {
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "sales_write")
                )
                .Options;
        using SaleWriteDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);

        IMigrator migrator = context.GetService<IMigrator>();
        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE sales", script);
        Assert.Contains("CREATE TABLE sale_orders", script);
        Assert.Contains("CREATE TABLE sale_payments", script);
        Assert.Contains("TipAmount", script);
        Assert.Contains("DiscountPercentage", script);
        Assert.Contains("BusinessDate", script);
        Assert.Contains("DROP COLUMN \"DiscountPercentage\"", rollback);
    }

    private static void AssertSnapshotMatchesCurrentModel(DbContext context)
    {
        ModelSnapshot snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var snapshotModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();

        Assert.False(
            differ.HasDifferences(
                snapshotModel.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel()
            )
        );
    }
}
