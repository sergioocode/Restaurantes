using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Payments.Infrastructure.Persistence.Write;

#nullable disable

namespace Restaurantes.Payments.Infrastructure.Persistence.Write.Migrations;

public sealed partial class AddPartialPaymentCaptures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "AmountPaid",
            table: "payable_orders",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<decimal>(
            name: "TipAmountPaid",
            table: "payable_orders",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );

        migrationBuilder.Sql(
            "UPDATE payable_orders SET \"AmountPaid\" = \"Amount\" WHERE \"Status\" = 2"
        );

        migrationBuilder.CreateTable(
            name: "payment_captures",
            columns: table => new
            {
                PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                TipAmount = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                Method = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                ExternalReference = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                CapturedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_captures", x => x.PaymentId);
                table.ForeignKey(
                    name: "FK_payment_captures_payable_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "payable_orders",
                    principalColumn: "OrderId",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_payment_captures_OrderId_IdempotencyKey",
            table: "payment_captures",
            columns: new[] { "OrderId", "IdempotencyKey" },
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "payment_captures");
        migrationBuilder.DropColumn(name: "AmountPaid", table: "payable_orders");
        migrationBuilder.DropColumn(name: "TipAmountPaid", table: "payable_orders");
    }
}
