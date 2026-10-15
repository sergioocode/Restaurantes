using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Sales.Infrastructure.Persistence.Write;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(SaleWriteDbContext))]
[Migration("20261013100001_AddSaleDiscountSnapshot")]
public sealed class AddSaleDiscountSnapshot : Migration
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
        migrationBuilder.AddColumn<string>(
            "DiscountName",
            "sale_orders",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true
        );
        migrationBuilder.AddColumn<decimal>(
            "DiscountPercentage",
            "sale_orders",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<decimal>(
            "DiscountAmount",
            "sale_orders",
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
        migrationBuilder.DropColumn("DiscountName", "sale_orders");
        migrationBuilder.DropColumn("DiscountPercentage", "sale_orders");
        migrationBuilder.DropColumn("DiscountAmount", "sale_orders");
    }
}
