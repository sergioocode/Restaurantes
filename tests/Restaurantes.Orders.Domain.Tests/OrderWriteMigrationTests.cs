using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderWriteMigrationTests
{
    [Fact]
    public void Write_migrations_preserve_order_payment_kitchen_and_zone_schema()
    {
        DbContextOptions<OrderWriteDbContext> options =
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "orders_write")
                )
                .Options;
        using OrderWriteDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);

        IMigrator migrator = context.GetService<IMigrator>();
        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE orders", script);
        Assert.Contains("CREATE TABLE order_lines", script);
        Assert.Contains("CREATE TABLE order_payments", script);
        Assert.Contains("\"ZoneId\"", script);
        Assert.Contains("\"Method\"", script);
        Assert.Contains("\"KitchenStatus\"", script);
        Assert.Contains("DROP COLUMN \"ZoneId\"", rollback);
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
