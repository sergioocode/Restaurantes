using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.CashRegister.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCashRegister : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cash_register_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                OpeningFloat = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                OpenedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                OpenedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OpenedByName = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: false
                ),
                ClosedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ClosedByName = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: true
                ),
                CountedCash = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: true
                ),
                ExpectedCashAtClose = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: true
                ),
                DifferenceAtClose = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: true
                ),
                Version = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cash_register_sessions", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProcessedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "cash_movements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CashRegisterSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Amount = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                Reference = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                OccurredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cash_movements", x => x.Id);
                table.ForeignKey(
                    name: "FK_cash_movements_cash_register_sessions_CashRegisterSessionId",
                    column: x => x.CashRegisterSessionId,
                    principalTable: "cash_register_sessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_cash_movements_CashRegisterSessionId",
            table: "cash_movements",
            column: "CashRegisterSessionId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_cash_movements_PaymentId_Type",
            table: "cash_movements",
            columns: new[] { "PaymentId", "Type" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_cash_register_sessions_RestaurantId_BusinessDate",
            table: "cash_register_sessions",
            columns: new[] { "RestaurantId", "BusinessDate" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "cash_movements");

        migrationBuilder.DropTable(name: "inbox_messages");

        migrationBuilder.DropTable(name: "cash_register_sessions");
    }
}
