using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Payments.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddDiningSessionToPayableOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DiningSessionId",
            table: "payable_orders",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
        );

        migrationBuilder.AddColumn<Guid>(
            name: "TableId",
            table: "payable_orders",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_payable_orders_DiningSessionId",
            table: "payable_orders",
            column: "DiningSessionId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_payable_orders_DiningSessionId",
            table: "payable_orders"
        );

        migrationBuilder.DropColumn(name: "DiningSessionId", table: "payable_orders");

        migrationBuilder.DropColumn(name: "TableId", table: "payable_orders");
    }
}
