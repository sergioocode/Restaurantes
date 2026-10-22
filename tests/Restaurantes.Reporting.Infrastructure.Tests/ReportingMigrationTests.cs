using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingMigrationTests
{
    [Fact]
    public void Read_migrations_preserve_completed_sale_reporting_and_rollback()
    {
        DbContextOptions<ReportingReadDbContext> options =
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql =>
                        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "reporting_read")
                )
                .Options;
        using ReportingReadDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();

        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE completed_sale_facts", script);
        Assert.Contains("PaymentMethodsJson", script);
        Assert.Contains("BusinessDate", script);
        Assert.Contains("DROP COLUMN \"PaymentMethodsJson\"", rollback);
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
