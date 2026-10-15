using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(SaleReadDbContext))]
[Migration("20261012110001_AddSaleTipTotal")]
public partial class AddSaleTipTotal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "TipTotal",
            table: "sales",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TipTotal", table: "sales");
    }
}
