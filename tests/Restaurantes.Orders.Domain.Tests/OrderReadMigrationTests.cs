using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderReadMigrationTests
{
    [Fact]
    public void Read_migrations_preserve_kitchen_order_zone_projection_and_rollback()
    {
        DbContextOptions<OrderReadDbContext> options =
            new DbContextOptionsBuilder<OrderReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "orders_read")
                )
                .Options;
        using OrderReadDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();

        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE kitchen_order_views", script);
        Assert.Contains("\"ZoneId\"", script);
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
