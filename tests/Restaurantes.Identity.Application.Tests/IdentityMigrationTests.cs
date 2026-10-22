using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Identity.Infrastructure.Persistence;
using Xunit;

namespace Restaurantes.Identity.Application.Tests;

public sealed class IdentityMigrationTests
{
    [Fact]
    public void Migrations_preserve_external_account_scope_and_login_ticket_schema()
    {
        DbContextOptions<IdentityWriteDbContext> options =
            new DbContextOptionsBuilder<IdentityWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")
                )
                .Options;
        using IdentityWriteDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);

        IMigrator migrator = context.GetService<IMigrator>();
        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("RENAME TO authorized_accounts", script);
        Assert.Contains("CREATE TABLE login_tickets", script);
        Assert.Contains("CK_authorized_accounts_restaurant_scope", script);
        Assert.Contains("ProviderSubject", script);
        Assert.Contains("ConsumedAtUtc", script);
        Assert.Contains("DROP TABLE login_tickets", rollback);
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
