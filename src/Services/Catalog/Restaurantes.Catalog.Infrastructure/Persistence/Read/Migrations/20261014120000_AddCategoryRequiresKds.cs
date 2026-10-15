using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(CatalogReadDbContext))]
[Migration("20261014120000_AddCategoryRequiresKds")]
public sealed class AddCategoryRequiresKds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "RequiresKds",
            table: "category_views",
            type: "boolean",
            nullable: false,
            defaultValue: true
        );
        migrationBuilder.AddColumn<bool>(
            name: "RequiresKds",
            table: "menu_item_views",
            type: "boolean",
            nullable: false,
            defaultValue: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RequiresKds", table: "category_views");
        migrationBuilder.DropColumn(name: "RequiresKds", table: "menu_item_views");
    }
}
