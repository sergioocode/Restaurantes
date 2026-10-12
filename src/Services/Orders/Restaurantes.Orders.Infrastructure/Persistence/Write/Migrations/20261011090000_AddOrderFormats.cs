using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(OrderWriteDbContext))]
[Migration("20261011090000_AddOrderFormats")]
public sealed class AddOrderFormats : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DefaultFormat",
            table: "catalog_items",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "PLATO"
        );
        migrationBuilder.AddColumn<string>(
            name: "FormatsJson",
            table: "catalog_items",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]"
        );
        migrationBuilder.AddColumn<string>(
            name: "Format",
            table: "order_lines",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: ""
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Format", table: "order_lines");
        migrationBuilder.DropColumn(name: "FormatsJson", table: "catalog_items");
        migrationBuilder.DropColumn(name: "DefaultFormat", table: "catalog_items");
    }
}
