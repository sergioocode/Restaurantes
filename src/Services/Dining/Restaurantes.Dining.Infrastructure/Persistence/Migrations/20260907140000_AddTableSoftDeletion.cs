using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

public partial class AddTableSoftDeletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            table: "restaurant_tables",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.DropIndex(
            name: "IX_restaurant_tables_RestaurantId_Code",
            table: "restaurant_tables"
        );
        migrationBuilder.CreateIndex(
            name: "IX_restaurant_tables_RestaurantId_Code",
            table: "restaurant_tables",
            columns: new[] { "RestaurantId", "Code" },
            unique: true,
            filter: "\"DeletedAtUtc\" IS NULL"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverting would revive deleted locations and may collide with reused codes. Require an explicit data plan.
        migrationBuilder.Sql(
            """
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM restaurant_tables WHERE "DeletedAtUtc" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot revert table deletion while deleted locations exist. Preserve their history and resolve them explicitly first.';
                END IF;
            END $$;
            """
        );
        migrationBuilder.DropIndex(
            name: "IX_restaurant_tables_RestaurantId_Code",
            table: "restaurant_tables"
        );
        migrationBuilder.CreateIndex(
            name: "IX_restaurant_tables_RestaurantId_Code",
            table: "restaurant_tables",
            columns: new[] { "RestaurantId", "Code" },
            unique: true
        );
        migrationBuilder.DropColumn(name: "DeletedAtUtc", table: "restaurant_tables");
    }
}
