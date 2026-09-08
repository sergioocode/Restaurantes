using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPaymentTransactionToReporting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "PaymentTransactionId",
            table: "order_reporting_facts",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_order_reporting_facts_PaymentTransactionId",
            table: "order_reporting_facts",
            column: "PaymentTransactionId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_order_reporting_facts_PaymentTransactionId",
            table: "order_reporting_facts"
        );

        migrationBuilder.DropColumn(name: "PaymentTransactionId", table: "order_reporting_facts");
    }
}
