using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Api.Write.Migrations;

public partial class AddZoneSoftDeletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            table: "dining_zones",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.DropIndex(
            name: "IX_dining_zones_RestaurantId_Name",
            table: "dining_zones"
        );
        migrationBuilder.CreateIndex(
            name: "IX_dining_zones_RestaurantId_Name",
            table: "dining_zones",
            columns: new[] { "RestaurantId", "Name" },
            unique: true,
            filter: "\"DeletedAtUtc\" IS NULL"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM dining_zones WHERE "DeletedAtUtc" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot revert zone deletion while deleted zones exist. Resolve their history explicitly first.';
                END IF;
            END $$;
            """
        );
        migrationBuilder.DropIndex(
            name: "IX_dining_zones_RestaurantId_Name",
            table: "dining_zones"
        );
        migrationBuilder.CreateIndex(
            name: "IX_dining_zones_RestaurantId_Name",
            table: "dining_zones",
            columns: new[] { "RestaurantId", "Name" },
            unique: true
        );
        migrationBuilder.DropColumn(name: "DeletedAtUtc", table: "dining_zones");
    }
}
