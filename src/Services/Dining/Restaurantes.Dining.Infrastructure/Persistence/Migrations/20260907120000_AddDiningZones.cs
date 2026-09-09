using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

public partial class AddDiningZones : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "dining_zones",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dining_zones", x => x.Id);
                table.UniqueConstraint(
                    "AK_dining_zones_RestaurantId_Id",
                    x => new { x.RestaurantId, x.Id }
                );
            }
        );
        migrationBuilder.CreateIndex(
            name: "IX_dining_zones_RestaurantId_Name",
            table: "dining_zones",
            columns: new[] { "RestaurantId", "Name" },
            unique: true
        );
        migrationBuilder.AddColumn<Guid>(
            name: "ZoneId",
            table: "restaurant_tables",
            type: "uuid",
            nullable: true
        );

        // Keep table IDs, QR codes and session history intact; backfill before enforcing the FK.
        migrationBuilder.Sql(
            """
            INSERT INTO dining_zones ("Id", "RestaurantId", "Name", "SortOrder")
            SELECT gen_random_uuid(), "RestaurantId", 'General', 0
            FROM restaurant_tables GROUP BY "RestaurantId";

            UPDATE restaurant_tables AS t
            SET "ZoneId" = z."Id"
            FROM dining_zones AS z
            WHERE z."RestaurantId" = t."RestaurantId";
            """
        );

        migrationBuilder.AlterColumn<Guid>(
            name: "ZoneId",
            table: "restaurant_tables",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );
        migrationBuilder.CreateIndex(
            name: "IX_restaurant_tables_RestaurantId_ZoneId",
            table: "restaurant_tables",
            columns: new[] { "RestaurantId", "ZoneId" }
        );
        migrationBuilder.AddForeignKey(
            name: "FK_restaurant_tables_dining_zones_RestaurantId_ZoneId",
            table: "restaurant_tables",
            columns: new[] { "RestaurantId", "ZoneId" },
            principalTable: "dining_zones",
            principalColumns: new[] { "RestaurantId", "Id" },
            onDelete: ReferentialAction.Restrict
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_restaurant_tables_dining_zones_RestaurantId_ZoneId",
            table: "restaurant_tables"
        );
        migrationBuilder.DropIndex(
            name: "IX_restaurant_tables_RestaurantId_ZoneId",
            table: "restaurant_tables"
        );
        migrationBuilder.DropColumn(name: "ZoneId", table: "restaurant_tables");
        migrationBuilder.DropTable(name: "dining_zones");
    }
}
