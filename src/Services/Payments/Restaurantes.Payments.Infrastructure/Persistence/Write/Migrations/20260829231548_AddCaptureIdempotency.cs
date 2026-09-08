using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Payments.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddCaptureIdempotency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CaptureIdempotencyKey",
            table: "payable_orders",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_payable_orders_CaptureIdempotencyKey",
            table: "payable_orders",
            column: "CaptureIdempotencyKey"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_payable_orders_CaptureIdempotencyKey",
            table: "payable_orders"
        );

        migrationBuilder.DropColumn(name: "CaptureIdempotencyKey", table: "payable_orders");
    }
}
