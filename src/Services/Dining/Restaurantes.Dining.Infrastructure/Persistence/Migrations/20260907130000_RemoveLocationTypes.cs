using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

public partial class RemoveLocationTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Zones are the only classification. Keep location IDs, names, QR codes and sessions.
        migrationBuilder.DropColumn(name: "LocationType", table: "restaurant_tables");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The obsolete classification cannot be recovered; restored locations use the old default.
        migrationBuilder.AddColumn<string>(
            name: "LocationType",
            table: "restaurant_tables",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Table"
        );
    }
}
