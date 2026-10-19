using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261019120000_AddKitchenStationTicketPrinting")]
public sealed class AddKitchenStationTicketPrinting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "PrintTicket",
            table: "restaurant_kitchen_stations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "PrintTicket", table: "restaurant_kitchen_stations");
}
