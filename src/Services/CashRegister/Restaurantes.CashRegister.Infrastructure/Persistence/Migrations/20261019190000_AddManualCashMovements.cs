using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.CashRegister.Infrastructure.Persistence;

#nullable disable

namespace Restaurantes.CashRegister.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CashRegisterDbContext))]
[Migration("20261019190000_AddManualCashMovements")]
public partial class AddManualCashMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "PaymentId",
            table: "cash_movements",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid"
        );

        migrationBuilder.AlterColumn<Guid>(
            name: "OrderId",
            table: "cash_movements",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid"
        );

        migrationBuilder.AddColumn<string>(
            name: "Category",
            table: "cash_movements",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<Guid>(
            name: "RecordedByUserId",
            table: "cash_movements",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "RecordedByName",
            table: "cash_movements",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM cash_movements WHERE \"PaymentId\" IS NULL;");

        migrationBuilder.DropColumn(name: "RecordedByName", table: "cash_movements");
        migrationBuilder.DropColumn(name: "RecordedByUserId", table: "cash_movements");
        migrationBuilder.DropColumn(name: "Category", table: "cash_movements");

        migrationBuilder.AlterColumn<Guid>(
            name: "OrderId",
            table: "cash_movements",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );

        migrationBuilder.AlterColumn<Guid>(
            name: "PaymentId",
            table: "cash_movements",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );
    }
}
