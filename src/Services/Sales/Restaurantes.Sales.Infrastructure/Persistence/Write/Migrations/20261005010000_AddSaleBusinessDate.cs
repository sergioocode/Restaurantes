using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(SaleWriteDbContext))]
[Migration("20261005010000_AddSaleBusinessDate")]
public partial class AddSaleBusinessDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "BusinessDate",
            table: "sales",
            type: "date",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BusinessDate", table: "sales");
    }
}
