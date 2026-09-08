using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddCatalogOrderSourceAndPrices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PaymentTiming",
            table: "orders",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "Source",
            table: "orders",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<decimal>(
            name: "UnitPrice",
            table: "order_lines",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );

        migrationBuilder.CreateTable(
            name: "catalog_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductName = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                Price = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryName = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: false
                ),
                StationCode = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                StationName = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_catalog_items", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_catalog_items_RestaurantId_ProductId",
            table: "catalog_items",
            columns: new[] { "RestaurantId", "ProductId" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "catalog_items");

        migrationBuilder.DropColumn(name: "PaymentTiming", table: "orders");

        migrationBuilder.DropColumn(name: "Source", table: "orders");

        migrationBuilder.DropColumn(name: "UnitPrice", table: "order_lines");
    }
}
