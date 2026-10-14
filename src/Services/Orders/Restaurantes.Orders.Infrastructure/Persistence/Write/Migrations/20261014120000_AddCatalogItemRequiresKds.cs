using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(OrderWriteDbContext))]
[Migration("20261014120000_AddCatalogItemRequiresKds")]
public sealed class AddCatalogItemRequiresKds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "RequiresKds",
            table: "catalog_items",
            type: "boolean",
            nullable: false,
            defaultValue: true
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "RequiresKds", table: "catalog_items");
}
