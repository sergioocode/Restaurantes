using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Sales.Infrastructure.Persistence.Read;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(SaleReadDbContext))]
[Migration("20261013100002_AddSaleReadDiscountSnapshot")]
public sealed class AddSaleReadDiscountSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            "DiscountName",
            "sales",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true
        );
        migrationBuilder.AddColumn<decimal>(
            "DiscountPercentage",
            "sales",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<decimal>(
            "DiscountAmount",
            "sales",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("DiscountName", "sales");
        migrationBuilder.DropColumn("DiscountPercentage", "sales");
        migrationBuilder.DropColumn("DiscountAmount", "sales");
    }
}
