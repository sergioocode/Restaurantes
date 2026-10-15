using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(OrderWriteDbContext))]
[Migration("20261009190000_AddOrderLineKitchenProgress")]
public sealed class AddOrderLineKitchenProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "KitchenStatus",
            table: "order_lines",
            type: "integer",
            nullable: false,
            defaultValue: 1
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "PreparationStartedAtUtc",
            table: "order_lines",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "ReadyAtUtc",
            table: "order_lines",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "DispatchedAtUtc",
            table: "order_lines",
            type: "timestamp with time zone",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DispatchedAtUtc", table: "order_lines");
        migrationBuilder.DropColumn(name: "ReadyAtUtc", table: "order_lines");
        migrationBuilder.DropColumn(name: "PreparationStartedAtUtc", table: "order_lines");
        migrationBuilder.DropColumn(name: "KitchenStatus", table: "order_lines");
    }
}
