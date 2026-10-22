using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Restaurantes.CashRegister.Domain;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterEfMappingAndMigrationTests
{
    [Fact]
    public void Cash_movement_model_maps_manual_and_sale_fields()
    {
        DbContextOptions<CashRegisterDbContext> options =
            new DbContextOptionsBuilder<CashRegisterDbContext>()
                .UseNpgsql("Host=localhost;Database=cash;Username=cash;Password=cash")
                .Options;
        using CashRegisterDbContext db = new(options);

        IEntityType movement = db.Model.FindEntityType(typeof(CashMovement))!;

        Assert.Equal("cash_movements", movement.GetTableName());
        Assert.True(movement.FindProperty(nameof(CashMovement.SaleId))!.IsNullable);
        Assert.Equal(80, movement.FindProperty(nameof(CashMovement.Category))!.GetMaxLength());
        Assert.Contains(
            movement.GetIndexes(),
            index => index.Properties.Any(property => property.Name == nameof(CashMovement.SaleId))
        );
        CashMovement newMovement = new();
        Assert.Equal(string.Empty, newMovement.Method);
        Assert.Equal(string.Empty, newMovement.Category);
        Assert.Equal(string.Empty, newMovement.Reference);
    }

    [Fact]
    public void Cash_register_migrations_generate_operational_and_reversible_schema()
    {
        DbContextOptions<CashRegisterDbContext> options =
            new DbContextOptionsBuilder<CashRegisterDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                    npgsql =>
                        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "cash_register")
                )
                .Options;
        using CashRegisterDbContext context = new(options);
        AssertSnapshotMatchesCurrentModel(context);
        IMigrator migrator = context.GetService<IMigrator>();

        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(context.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE cash_register_sessions", script);
        Assert.Contains("ExpectedTotalAtClose", script);
        Assert.Contains("cash_movements", script);
        Assert.Contains("IX_cash_movements_SaleId", script);
        Assert.Contains("DROP TABLE cash_movements", rollback);
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

    [Theory]
    [InlineData("AddManualCashMovements", 5, 6)]
    [InlineData("AddSaleIdToCashMovements", 2, 2)]
    public void Cash_movement_migrations_define_up_and_down_operations(
        string migrationName,
        int upOperationCount,
        int downOperationCount
    )
    {
        Type migrationType = typeof(CashRegisterDbContext).Assembly.GetType(
            $"Restaurantes.CashRegister.Infrastructure.Persistence.Migrations.{migrationName}",
            throwOnError: true
        )!;
        Migration migration = (Migration)Activator.CreateInstance(migrationType)!;
        MigrationBuilder upBuilder = new("Npgsql.EntityFrameworkCore.PostgreSQL");
        migrationType
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [upBuilder]);
        MigrationBuilder downBuilder = new("Npgsql.EntityFrameworkCore.PostgreSQL");
        migrationType
            .GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [downBuilder]);

        Assert.Equal(upOperationCount, upBuilder.Operations.Count);
        Assert.Equal(downOperationCount, downBuilder.Operations.Count);
        if (migrationName == "AddSaleIdToCashMovements")
        {
            Assert.Contains(
                upBuilder.Operations.OfType<AddColumnOperation>(),
                operation => operation.Name == "SaleId" && operation.IsNullable
            );
            Assert.Contains(
                upBuilder.Operations.OfType<CreateIndexOperation>(),
                operation => operation.Name == "IX_cash_movements_SaleId"
            );
            Assert.Contains(
                downBuilder.Operations.OfType<DropIndexOperation>(),
                operation => operation.Name == "IX_cash_movements_SaleId"
            );
        }
        else
        {
            Assert.Contains(
                upBuilder.Operations.OfType<AddColumnOperation>(),
                operation => operation.Name == "Category" && operation.MaxLength == 80
            );
            Assert.Contains(
                downBuilder.Operations.OfType<SqlOperation>(),
                operation => operation.Sql.Contains("PaymentId", StringComparison.Ordinal)
            );
        }
    }
}
