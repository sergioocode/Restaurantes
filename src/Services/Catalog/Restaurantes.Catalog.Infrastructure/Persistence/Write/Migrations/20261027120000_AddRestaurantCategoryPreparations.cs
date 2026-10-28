using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261027120000_AddRestaurantCategoryPreparations")]
public sealed class AddRestaurantCategoryPreparations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "restaurant_category_preparations",
            columns: table => new
            {
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                StationCode = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                StationName = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_restaurant_category_preparations",
                    x => new { x.RestaurantId, x.CategoryId }
                );
                table.ForeignKey(
                    name: "FK_restaurant_category_preparations_categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );
        migrationBuilder.CreateIndex(
            name: "IX_restaurant_category_preparations_CategoryId",
            table: "restaurant_category_preparations",
            column: "CategoryId"
        );
        migrationBuilder.Sql(
            """
            INSERT INTO restaurant_category_preparations
                ("RestaurantId", "CategoryId", "StationCode", "StationName", "Version", "UpdatedAtUtc")
            SELECT restaurants."RestaurantId", categories."Id",
                   CASE WHEN categories."RequiresKds" AND station."Code" IS NOT NULL
                        THEN station."Code" ELSE '' END,
                   CASE WHEN categories."RequiresKds" AND station."Code" IS NOT NULL
                        THEN station."Name" ELSE '' END,
                   1, now()
            FROM (SELECT DISTINCT "RestaurantId" FROM restaurant_kitchen_stations) restaurants
            CROSS JOIN categories
            LEFT JOIN restaurant_kitchen_stations station
              ON station."RestaurantId" = restaurants."RestaurantId"
             AND station."Code" = categories."DefaultStationCode"
             AND station."IsActive" = TRUE
             AND station."IsPrimary" = FALSE
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "restaurant_category_preparations");
    }
}
