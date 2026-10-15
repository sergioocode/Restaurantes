using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Dining.Infrastructure.Persistence;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DiningDbContext))]
[Migration("20261008130000_MoveGuestCountRequestToZone")]
public sealed class MoveGuestCountRequestToZone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "RequestGuestCount",
            table: "dining_zones",
            type: "boolean",
            nullable: true
        );
        migrationBuilder.DropColumn(name: "RequestGuestCount", table: "restaurant_tables");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "RequestGuestCount",
            table: "restaurant_tables",
            type: "boolean",
            nullable: true
        );
        migrationBuilder.DropColumn(name: "RequestGuestCount", table: "dining_zones");
    }
}
