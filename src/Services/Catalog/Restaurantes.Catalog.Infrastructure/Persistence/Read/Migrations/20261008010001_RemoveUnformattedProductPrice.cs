using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(CatalogReadDbContext))]
[Migration("20261008010001_RemoveUnformattedProductPrice")]
public sealed class RemoveUnformattedProductPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "BasePrice", table: "product_views");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<decimal>(
            name: "BasePrice",
            table: "product_views",
            type: "numeric(12,2)",
            nullable: false,
            defaultValue: 0m
        );
}
