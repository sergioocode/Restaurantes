using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddKitchenLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeliveredAtUtc",
            table: "kitchen_order_views",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "PreparationStartedAtUtc",
            table: "kitchen_order_views",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "ReadyAtUtc",
            table: "kitchen_order_views",
            type: "timestamp with time zone",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DeliveredAtUtc", table: "kitchen_order_views");

        migrationBuilder.DropColumn(name: "PreparationStartedAtUtc", table: "kitchen_order_views");

        migrationBuilder.DropColumn(name: "ReadyAtUtc", table: "kitchen_order_views");
    }
}
