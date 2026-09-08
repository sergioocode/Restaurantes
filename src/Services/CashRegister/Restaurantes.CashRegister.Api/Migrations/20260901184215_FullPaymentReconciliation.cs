using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.CashRegister.Api.Migrations;

/// <inheritdoc />
public partial class FullPaymentReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ExpectedTotalAtClose",
            table: "cash_register_sessions",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: true
        );

        migrationBuilder.AddColumn<decimal>(
            name: "ReconciledTotalAtClose",
            table: "cash_register_sessions",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: true
        );

        migrationBuilder.CreateTable(
            name: "cash_register_reconciliations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CashRegisterSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                Method = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                ExpectedAmount = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                ReconciledAmount = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                Difference = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cash_register_reconciliations", x => x.Id);
                table.ForeignKey(
                    name: "FK_cash_register_reconciliations_cash_register_sessions_CashRe~",
                    column: x => x.CashRegisterSessionId,
                    principalTable: "cash_register_sessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_cash_register_reconciliations_CashRegisterSessionId_Method",
            table: "cash_register_reconciliations",
            columns: new[] { "CashRegisterSessionId", "Method" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "cash_register_reconciliations");

        migrationBuilder.DropColumn(name: "ExpectedTotalAtClose", table: "cash_register_sessions");

        migrationBuilder.DropColumn(
            name: "ReconciledTotalAtClose",
            table: "cash_register_sessions"
        );
    }
}
