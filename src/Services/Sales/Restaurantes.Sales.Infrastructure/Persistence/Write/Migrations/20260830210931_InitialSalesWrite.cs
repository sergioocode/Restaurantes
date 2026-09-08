using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class InitialSalesWrite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "delivered_order_markers",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                DeliveredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                LinesJson = table.Column<string>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_delivered_order_markers", x => x.OrderId);
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
            name: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Source = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                PaymentMethod = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                ExpectedOrderCount = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                Total = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                CompletedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "sale_orders",
            columns: table => new
            {
                SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                IsDelivered = table.Column<bool>(type: "boolean", nullable: false),
                PaidAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                DeliveredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                LinesJson = table.Column<string>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_orders", x => new { x.SaleId, x.OrderId });
                table.ForeignKey(
                    name: "FK_sale_orders_sales_SaleId",
                    column: x => x.SaleId,
                    principalTable: "sales",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedAtUtc_OccurredAtUtc",
            table: "outbox_messages",
            columns: new[] { "ProcessedAtUtc", "OccurredAtUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_sale_orders_OrderId",
            table: "sale_orders",
            column: "OrderId",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_sales_RestaurantId_CompletedAtUtc",
            table: "sales",
            columns: new[] { "RestaurantId", "CompletedAtUtc" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "delivered_order_markers");

        migrationBuilder.DropTable(name: "inbox_messages");

        migrationBuilder.DropTable(name: "outbox_messages");

        migrationBuilder.DropTable(name: "sale_orders");

        migrationBuilder.DropTable(name: "sales");
    }
}
