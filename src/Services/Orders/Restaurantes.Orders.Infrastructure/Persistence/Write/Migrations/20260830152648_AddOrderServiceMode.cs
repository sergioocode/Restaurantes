using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddOrderServiceMode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "DiningSessionId",
            table: "orders",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid"
        );

        migrationBuilder.Sql("ALTER TABLE orders ALTER COLUMN \"DiningSessionId\" DROP DEFAULT;");

        migrationBuilder.AddColumn<int>(
            name: "ServiceMode",
            table: "orders",
            type: "integer",
            nullable: false,
            defaultValue: 1
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ServiceMode", table: "orders");

        migrationBuilder.AlterColumn<Guid>(
            name: "DiningSessionId",
            table: "orders",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );
    }
}
