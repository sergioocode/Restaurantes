using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Payments.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddPaymentServiceMode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "DiningSessionId",
            table: "payable_orders",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid"
        );

        migrationBuilder.Sql(
            "ALTER TABLE payable_orders ALTER COLUMN \"DiningSessionId\" DROP DEFAULT;"
        );

        migrationBuilder.AddColumn<string>(
            name: "ServiceMode",
            table: "payable_orders",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "DineIn"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ServiceMode", table: "payable_orders");

        migrationBuilder.AlterColumn<Guid>(
            name: "DiningSessionId",
            table: "payable_orders",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );
    }
}
