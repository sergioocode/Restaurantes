using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(SaleWriteDbContext))]
[Migration("20261009090000_AddSalePayments")]
public partial class AddSalePayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sale_payments",
            columns: table => new
            {
                PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                Method = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                Amount = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_payments", x => x.PaymentId);
                table.ForeignKey(
                    name: "FK_sale_payments_sales_SaleId",
                    column: x => x.SaleId,
                    principalTable: "sales",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_sale_payments_SaleId",
            table: "sale_payments",
            column: "SaleId"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sale_payments");
    }
}
