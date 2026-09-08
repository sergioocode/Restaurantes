using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class InitialOrdersWrite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "orders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                TableId = table.Column<Guid>(type: "uuid", nullable: true),
                TableLabel = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                Status = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                SubmittedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(
                    type: "character varying(300)",
                    maxLength: 300,
                    nullable: false
                ),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                ProcessedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                Error = table.Column<string>(
                    type: "character varying(2000)",
                    maxLength: 2000,
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "order_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductName = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                Notes = table.Column<string>(
                    type: "character varying(500)",
                    maxLength: 500,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_order_lines_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_order_lines_OrderId",
            table: "order_lines",
            column: "OrderId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_orders_RestaurantId_Status_CreatedAtUtc",
            table: "orders",
            columns: new[] { "RestaurantId", "Status", "CreatedAtUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedAtUtc_OccurredAtUtc",
            table: "outbox_messages",
            columns: new[] { "ProcessedAtUtc", "OccurredAtUtc" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "order_lines");

        migrationBuilder.DropTable(name: "outbox_messages");

        migrationBuilder.DropTable(name: "orders");
    }
}
