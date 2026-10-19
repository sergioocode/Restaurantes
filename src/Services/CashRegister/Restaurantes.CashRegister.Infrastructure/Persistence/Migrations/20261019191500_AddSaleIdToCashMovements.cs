using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.CashRegister.Infrastructure.Persistence;

#nullable disable

namespace Restaurantes.CashRegister.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CashRegisterDbContext))]
[Migration("20261019191500_AddSaleIdToCashMovements")]
public partial class AddSaleIdToCashMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SaleId",
            table: "cash_movements",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_cash_movements_SaleId",
            table: "cash_movements",
            column: "SaleId"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_cash_movements_SaleId", table: "cash_movements");
        migrationBuilder.DropColumn(name: "SaleId", table: "cash_movements");
    }
}
