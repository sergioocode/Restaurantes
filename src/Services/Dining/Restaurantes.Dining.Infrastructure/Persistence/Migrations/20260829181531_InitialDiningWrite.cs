using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialDiningWrite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
            name: "pending_payments",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pending_payments", x => x.OrderId);
            }
        );

        migrationBuilder.CreateTable(
            name: "restaurant_tables",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                Label = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_restaurant_tables", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "dining_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                TableId = table.Column<Guid>(type: "uuid", nullable: false),
                Source = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                Status = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false
                ),
                Version = table.Column<int>(type: "integer", nullable: false),
                OpenedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                ClosedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dining_sessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_dining_sessions_restaurant_tables_TableId",
                    column: x => x.TableId,
                    principalTable: "restaurant_tables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "dining_session_orders",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                DiningSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                PaymentStatus = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false
                ),
                AddedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dining_session_orders", x => x.OrderId);
                table.ForeignKey(
                    name: "FK_dining_session_orders_dining_sessions_DiningSessionId",
                    column: x => x.DiningSessionId,
                    principalTable: "dining_sessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_dining_session_orders_DiningSessionId",
            table: "dining_session_orders",
            column: "DiningSessionId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_dining_sessions_TableId_Status",
            table: "dining_sessions",
            columns: new[] { "TableId", "Status" },
            unique: true,
            filter: "\"Status\" = 'Open'"
        );

        migrationBuilder.CreateIndex(
            name: "IX_restaurant_tables_RestaurantId_Code",
            table: "restaurant_tables",
            columns: new[] { "RestaurantId", "Code" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "dining_session_orders");

        migrationBuilder.DropTable(name: "inbox_messages");

        migrationBuilder.DropTable(name: "pending_payments");

        migrationBuilder.DropTable(name: "dining_sessions");

        migrationBuilder.DropTable(name: "restaurant_tables");
    }
}
