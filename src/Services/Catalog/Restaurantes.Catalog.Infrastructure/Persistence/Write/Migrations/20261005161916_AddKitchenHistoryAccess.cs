using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddKitchenHistoryAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CanViewHistory", table: "restaurant_kitchen_stations");
    }
}
