using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(OrderWriteDbContext))]
[Migration("20261014133000_AddTaxRateToOrders")]
public sealed class AddTaxRateToOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "TaxRate",
            table: "catalog_items",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: true
        );

        migrationBuilder.AddColumn<decimal>(
            name: "TaxRate",
            table: "order_lines",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TaxRate", table: "catalog_items");
        migrationBuilder.DropColumn(name: "TaxRate", table: "order_lines");
    }
}
