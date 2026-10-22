using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleReadMigrationTests
{
    [Fact]
    public void Read_migrations_preserve_payment_method_tip_and_discount_snapshots()
    {
        DbContextOptions<SaleReadDbContext> options =
            new DbContextOptionsBuilder<SaleReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "sales_read")
                )
                .Options;
        using SaleReadDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();

        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE sales", script);
        Assert.Contains("PaymentMethodsJson", script);
        Assert.Contains("TipTotal", script);
        Assert.Contains("DiscountPercentage", script);
        Assert.Contains("DROP COLUMN \"TipTotal\"", rollback);
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
