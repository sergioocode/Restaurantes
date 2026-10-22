using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentWriteMigrationTests
{
    [Fact]
    public void Write_migrations_preserve_capture_idempotency_and_partial_payment_schema()
    {
        DbContextOptions<PaymentWriteDbContext> options =
            new DbContextOptionsBuilder<PaymentWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql =>
                        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "payments_write")
                )
                .Options;
        using PaymentWriteDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);

        IMigrator migrator = context.GetService<IMigrator>();
        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE payable_orders", script);
        Assert.Contains("CREATE TABLE payment_captures", script);
        Assert.Contains("CaptureIdempotencyKey", script);
        Assert.Contains("DiningSessionId", script);
        Assert.Contains("TipAmount", script);
        Assert.Contains("IX_payment_captures_OrderId_IdempotencyKey", script);
        Assert.Contains("DROP TABLE payment_captures", rollback);
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
