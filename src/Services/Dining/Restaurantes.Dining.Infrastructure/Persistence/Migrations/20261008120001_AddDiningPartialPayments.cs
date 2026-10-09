using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Dining.Infrastructure.Persistence;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DiningDbContext))]
[Migration("20261008120001_AddDiningPartialPayments")]
public sealed class AddDiningPartialPayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "AmountPaid",
            table: "dining_session_orders",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<decimal>(
            name: "TipPaid",
            table: "dining_session_orders",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<decimal>(
            name: "AmountPaid",
            table: "pending_payments",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<decimal>(
            name: "TipPaid",
            table: "pending_payments",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.Sql(
            "UPDATE dining_session_orders SET \"AmountPaid\" = \"Amount\" WHERE \"PaymentStatus\" = 'Paid'"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AmountPaid", table: "pending_payments");
        migrationBuilder.DropColumn(name: "AmountPaid", table: "dining_session_orders");
        migrationBuilder.DropColumn(name: "TipPaid", table: "pending_payments");
        migrationBuilder.DropColumn(name: "TipPaid", table: "dining_session_orders");
    }
}
