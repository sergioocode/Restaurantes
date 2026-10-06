using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddKitchenHistoryAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CanViewHistory", table: "kitchen_station_views");
    }
}
