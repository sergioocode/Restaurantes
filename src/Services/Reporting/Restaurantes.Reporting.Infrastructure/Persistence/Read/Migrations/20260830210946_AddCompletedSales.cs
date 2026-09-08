using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCompletedSales : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "completed_sale_facts",
            columns: table => new
            {
                SaleId = table.Column<Guid>(type: "uuid", nullable: false),
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
                CompletedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                OrderIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                LinesJson = table.Column<string>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_completed_sale_facts", x => x.SaleId);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_completed_sale_facts_RestaurantId_CompletedAtUtc",
            table: "completed_sale_facts",
            columns: new[] { "RestaurantId", "CompletedAtUtc" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "completed_sale_facts");
    }
}
