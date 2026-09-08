using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddKitchenLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeliveredAtUtc",
            table: "orders",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "PreparationStartedAtUtc",
            table: "orders",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "ReadyAtUtc",
            table: "orders",
            type: "timestamp with time zone",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DeliveredAtUtc", table: "orders");

        migrationBuilder.DropColumn(name: "PreparationStartedAtUtc", table: "orders");

        migrationBuilder.DropColumn(name: "ReadyAtUtc", table: "orders");
    }
}
