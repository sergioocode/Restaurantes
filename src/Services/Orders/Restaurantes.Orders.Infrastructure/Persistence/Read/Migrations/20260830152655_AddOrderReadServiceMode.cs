using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddOrderReadServiceMode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "DiningSessionId",
            table: "kitchen_order_views",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid"
        );

        migrationBuilder.Sql(
            "ALTER TABLE kitchen_order_views ALTER COLUMN \"DiningSessionId\" DROP DEFAULT;"
        );

        migrationBuilder.AddColumn<string>(
            name: "ServiceMode",
            table: "kitchen_order_views",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "DineIn"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ServiceMode", table: "kitchen_order_views");

        migrationBuilder.AlterColumn<Guid>(
            name: "DiningSessionId",
            table: "kitchen_order_views",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );
    }
}
