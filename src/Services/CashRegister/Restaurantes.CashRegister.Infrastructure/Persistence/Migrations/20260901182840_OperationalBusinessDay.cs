using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.CashRegister.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class OperationalBusinessDay : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_cash_register_sessions_RestaurantId_BusinessDate",
            table: "cash_register_sessions"
        );

        migrationBuilder.AddColumn<string>(
            name: "Method",
            table: "cash_movements",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: ""
        );

        // Before this migration the projection only consumed cash events.
        migrationBuilder.Sql(
            "UPDATE cash_movements SET \"Method\" = 'Cash' WHERE \"Method\" = '';"
        );

        migrationBuilder.CreateIndex(
            name: "IX_cash_register_sessions_RestaurantId",
            table: "cash_register_sessions",
            column: "RestaurantId",
            unique: true,
            filter: "\"Status\" = 0"
        );

        migrationBuilder.CreateIndex(
            name: "IX_cash_register_sessions_RestaurantId_BusinessDate",
            table: "cash_register_sessions",
            columns: new[] { "RestaurantId", "BusinessDate" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_cash_register_sessions_RestaurantId",
            table: "cash_register_sessions"
        );

        migrationBuilder.DropIndex(
            name: "IX_cash_register_sessions_RestaurantId_BusinessDate",
            table: "cash_register_sessions"
        );

        migrationBuilder.DropColumn(name: "Method", table: "cash_movements");

        migrationBuilder.CreateIndex(
            name: "IX_cash_register_sessions_RestaurantId_BusinessDate",
            table: "cash_register_sessions",
            columns: new[] { "RestaurantId", "BusinessDate" },
            unique: true
        );
    }
}
