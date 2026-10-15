using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(ReportingReadDbContext))]
[Migration("20261009090001_AddSalePaymentMethods")]
public partial class AddSalePaymentMethods : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PaymentMethodsJson",
            table: "completed_sale_facts",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PaymentMethodsJson", table: "completed_sale_facts");
    }
}
