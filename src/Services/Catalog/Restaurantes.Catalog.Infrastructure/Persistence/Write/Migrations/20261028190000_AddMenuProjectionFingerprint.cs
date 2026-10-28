using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261028190000_AddMenuProjectionFingerprint")]
public sealed class AddMenuProjectionFingerprint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProjectionFingerprint",
            table: "restaurant_menu_items",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: ""
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProjectionFingerprint", table: "restaurant_menu_items");
    }
}
