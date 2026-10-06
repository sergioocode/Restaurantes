using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261006120000_RemoveKitchenHistoryAccess")]
public sealed partial class RemoveKitchenHistoryAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CanViewHistory", table: "restaurant_kitchen_stations");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CanViewHistory",
            table: "restaurant_kitchen_stations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            "UPDATE catalog_write.restaurant_kitchen_stations SET \"CanViewHistory\" = TRUE WHERE \"IsPrimary\" = TRUE;"
        );
    }
}
