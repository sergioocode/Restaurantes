using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddKitchenStationDispatchConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPrimary",
            table: "kitchen_station_views",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<int>(
            name: "Priority",
            table: "kitchen_station_views",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<bool>(
            name: "RequiresPrimaryDispatch",
            table: "kitchen_station_views",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.CreateIndex(
            name: "IX_kitchen_station_views_RestaurantId",
            table: "kitchen_station_views",
            column: "RestaurantId",
            unique: true,
            filter: "\"IsPrimary\" = TRUE AND \"IsActive\" = TRUE"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_kitchen_station_views_RestaurantId",
            table: "kitchen_station_views"
        );

        migrationBuilder.DropColumn(name: "IsPrimary", table: "kitchen_station_views");

        migrationBuilder.DropColumn(name: "Priority", table: "kitchen_station_views");

        migrationBuilder.DropColumn(
            name: "RequiresPrimaryDispatch",
            table: "kitchen_station_views"
        );
    }
}
