using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

public sealed partial class QrReservationExpiration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "QrReservationExpiresAtUtc",
            table: "dining_sessions",
            type: "timestamp with time zone",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "QrReservationExpiresAtUtc", table: "dining_sessions");
    }
}
