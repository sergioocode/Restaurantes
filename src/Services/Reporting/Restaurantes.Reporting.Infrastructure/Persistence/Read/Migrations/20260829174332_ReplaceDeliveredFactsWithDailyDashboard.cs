using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ReplaceDeliveredFactsWithDailyDashboard : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "delivered_order_facts");

        migrationBuilder.CreateTable(
            name: "order_reporting_facts",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                TableLabel = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                Source = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                PaymentTiming = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                OrderStatus = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                PaymentStatus = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                PaymentMethod = table.Column<string>(
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
                Version = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                DeliveredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                PaidAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                RefundedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                SaleRecognizedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                LinesJson = table.Column<string>(type: "jsonb", nullable: false),
                StationsJson = table.Column<string>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_reporting_facts", x => x.OrderId);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_order_reporting_facts_RestaurantId_OrderStatus_UpdatedAtUtc",
            table: "order_reporting_facts",
            columns: new[] { "RestaurantId", "OrderStatus", "UpdatedAtUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_order_reporting_facts_SaleRecognizedAtUtc",
            table: "order_reporting_facts",
            column: "SaleRecognizedAtUtc"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "order_reporting_facts");

        migrationBuilder.CreateTable(
            name: "delivered_order_facts",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                DeliveredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                LinesJson = table.Column<string>(type: "jsonb", nullable: false),
                PaymentTiming = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Source = table.Column<string>(
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
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_delivered_order_facts", x => x.OrderId);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_delivered_order_facts_RestaurantId_DeliveredAtUtc",
            table: "delivered_order_facts",
            columns: new[] { "RestaurantId", "DeliveredAtUtc" }
        );
    }
}
