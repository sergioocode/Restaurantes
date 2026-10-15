using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(CatalogReadDbContext))]
[Migration("20261006120001_RemoveKitchenHistoryAccess")]
public sealed partial class RemoveKitchenHistoryAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CanViewHistory", table: "kitchen_station_views");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CanViewHistory",
            table: "kitchen_station_views",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            "UPDATE catalog_read.kitchen_station_views SET \"CanViewHistory\" = TRUE WHERE \"IsPrimary\" = TRUE;"
        );
    }
}
