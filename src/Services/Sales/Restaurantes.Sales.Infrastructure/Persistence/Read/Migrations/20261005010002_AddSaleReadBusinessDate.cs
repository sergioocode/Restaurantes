using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(SaleReadDbContext))]
[Migration("20261005010002_AddSaleReadBusinessDate")]
public partial class AddSaleReadBusinessDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "BusinessDate",
            table: "sales",
            type: "date",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_sales_BusinessDate_RestaurantId",
            table: "sales",
            columns: new[] { "BusinessDate", "RestaurantId" }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_sales_BusinessDate_RestaurantId", table: "sales");
        migrationBuilder.DropColumn(name: "BusinessDate", table: "sales");
    }
}
