using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Sales.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class InitialSalesRead : Migration
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
                Total = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                Status = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false
                ),
                OrderCount = table.Column<int>(type: "integer", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                OrderIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                LinesJson = table.Column<string>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales", x => x.Id);
            }
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
        migrationBuilder.DropTable(name: "inbox_messages");

        migrationBuilder.DropTable(name: "sales");
    }
}
