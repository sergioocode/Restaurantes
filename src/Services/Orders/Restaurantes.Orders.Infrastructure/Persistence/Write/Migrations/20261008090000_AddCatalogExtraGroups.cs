using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(OrderWriteDbContext))]
[Migration("20261008090000_AddCatalogExtraGroups")]
public sealed class AddCatalogExtraGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExtraGroupsJson",
            table: "catalog_items",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ExtraGroupsJson", table: "catalog_items");
    }
}
