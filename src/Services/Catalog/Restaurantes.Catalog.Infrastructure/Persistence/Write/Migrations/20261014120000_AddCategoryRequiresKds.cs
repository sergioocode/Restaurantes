using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261014120000_AddCategoryRequiresKds")]
public sealed class AddCategoryRequiresKds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "RequiresKds",
            table: "categories",
            type: "boolean",
            nullable: false,
            defaultValue: true
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "RequiresKds", table: "categories");
}
