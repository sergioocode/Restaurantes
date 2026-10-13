using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Dining.Infrastructure.Persistence;

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DiningDbContext))]
[Migration("20261013120000_AddDiningOrderOwnership")]
public sealed class AddDiningOrderOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CreatedByUserId",
            table: "dining_session_orders",
            type: "uuid",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "Source",
            table: "dining_session_orders",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CreatedByUserId", table: "dining_session_orders");
        migrationBuilder.DropColumn(name: "Source", table: "dining_session_orders");
    }
}
