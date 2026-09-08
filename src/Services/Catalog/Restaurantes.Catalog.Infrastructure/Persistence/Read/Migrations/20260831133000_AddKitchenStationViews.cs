using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddKitchenStationViews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "kitchen_station_views",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                Name = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kitchen_station_views", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_kitchen_station_views_RestaurantId_Code",
            table: "kitchen_station_views",
            columns: new[] { "RestaurantId", "Code" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "kitchen_station_views");
    }
}
