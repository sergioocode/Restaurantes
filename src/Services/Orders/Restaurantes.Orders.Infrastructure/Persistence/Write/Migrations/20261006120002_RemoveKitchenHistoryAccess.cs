using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(OrderWriteDbContext))]
[Migration("20261006120002_RemoveKitchenHistoryAccess")]
public sealed partial class RemoveKitchenHistoryAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CanViewHistory",
            table: "kitchen_station_configurations"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CanViewHistory",
            table: "kitchen_station_configurations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            "UPDATE orders_write.kitchen_station_configurations SET \"CanViewHistory\" = TRUE WHERE \"IsPrimary\" = TRUE;"
        );
    }
}
