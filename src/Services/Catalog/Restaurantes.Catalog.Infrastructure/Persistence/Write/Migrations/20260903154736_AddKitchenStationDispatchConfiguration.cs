using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddKitchenStationDispatchConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPrimary",
            table: "restaurant_kitchen_stations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<int>(
            name: "Priority",
            table: "restaurant_kitchen_stations",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<bool>(
            name: "RequiresPrimaryDispatch",
            table: "restaurant_kitchen_stations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.CreateIndex(
            name: "IX_restaurant_kitchen_stations_RestaurantId",
            table: "restaurant_kitchen_stations",
            column: "RestaurantId",
            unique: true,
            filter: "\"IsPrimary\" = TRUE AND \"IsActive\" = TRUE"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_restaurant_kitchen_stations_RestaurantId",
            table: "restaurant_kitchen_stations"
        );

        migrationBuilder.DropColumn(name: "IsPrimary", table: "restaurant_kitchen_stations");

        migrationBuilder.DropColumn(name: "Priority", table: "restaurant_kitchen_stations");

        migrationBuilder.DropColumn(
            name: "RequiresPrimaryDispatch",
            table: "restaurant_kitchen_stations"
        );
    }
}
