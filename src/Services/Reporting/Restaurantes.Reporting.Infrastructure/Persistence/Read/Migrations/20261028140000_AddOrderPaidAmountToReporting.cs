using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Migrations;

public partial class AddOrderPaidAmountToReporting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PaidAmount",
            table: "order_reporting_facts",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PaidAmount", table: "order_reporting_facts");
    }
}
