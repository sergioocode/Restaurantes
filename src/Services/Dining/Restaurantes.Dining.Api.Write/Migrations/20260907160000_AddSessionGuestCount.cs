using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Api.Write.Migrations;

public partial class AddSessionGuestCount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "RequestGuestCount",
            table: "restaurant_tables",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );
        // Existing sessions remain unaffected; new sessions snapshot the location setting.
        migrationBuilder.AddColumn<bool>(
            name: "RequestGuestCount",
            table: "dining_sessions",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );
        migrationBuilder.AddColumn<int>(
            name: "GuestCount",
            table: "dining_sessions",
            type: "integer",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GuestCount", table: "dining_sessions");
        migrationBuilder.DropColumn(name: "RequestGuestCount", table: "dining_sessions");
        migrationBuilder.DropColumn(name: "RequestGuestCount", table: "restaurant_tables");
    }
}
