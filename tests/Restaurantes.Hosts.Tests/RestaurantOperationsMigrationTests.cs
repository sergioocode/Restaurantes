using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class RestaurantOperationsMigrationTests
{
    [Fact]
    public void Write_migrations_preserve_company_relationship_and_rollback()
    {
        DbContextOptions<RestaurantWriteDbContext> options =
            new DbContextOptionsBuilder<RestaurantWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql =>
                        npgsql.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            "restaurant_operations"
                        )
                )
                .Options;
        using RestaurantWriteDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();

        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE companies", script);
        Assert.Contains("IX_companies_TaxId", script);
        Assert.Contains("FK_restaurants_companies_CompanyId", script);
        Assert.Contains("DROP TABLE companies", rollback);
    }

    [Fact]
    public void Read_migrations_preserve_company_projection_and_rollback()
    {
        DbContextOptions<RestaurantReadDbContext> options =
            new DbContextOptionsBuilder<RestaurantReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql =>
                        npgsql.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            "restaurant_operations_read"
                        )
                )
                .Options;
        using RestaurantReadDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();

        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE company_views", script);
        Assert.Contains("IX_company_views_TaxId", script);
        Assert.Contains("DROP TABLE company_views", rollback);
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
