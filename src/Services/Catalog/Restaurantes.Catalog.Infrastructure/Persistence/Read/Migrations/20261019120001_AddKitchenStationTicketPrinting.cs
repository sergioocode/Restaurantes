using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(CatalogReadDbContext))]
[Migration("20261019120001_AddKitchenStationTicketPrinting")]
public sealed class AddKitchenStationTicketPrinting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "PrintTicket",
            table: "kitchen_station_views",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "PrintTicket", table: "kitchen_station_views");
}
