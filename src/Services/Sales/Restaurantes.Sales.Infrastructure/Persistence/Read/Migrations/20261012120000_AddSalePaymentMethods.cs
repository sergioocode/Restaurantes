using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(SaleReadDbContext))]
[Migration("20261012120000_AddSalePaymentMethods")]
public partial class AddSalePaymentMethods : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PaymentMethodsJson",
            table: "sales",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PaymentMethodsJson", table: "sales");
    }
}
