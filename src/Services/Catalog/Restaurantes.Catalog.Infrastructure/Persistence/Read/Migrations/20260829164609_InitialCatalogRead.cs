using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class InitialCatalogRead : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "category_views",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                Name = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: false
                ),
                DefaultStationCode = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                DefaultStationName = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_category_views", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(
                    type: "character varying(300)",
                    maxLength: 300,
                    nullable: false
                ),
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
            name: "menu_item_views",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(
                    type: "character varying(60)",
                    maxLength: 60,
                    nullable: false
                ),
                ProductName = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryCode = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                CategoryName = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: false
                ),
                Price = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                PreparationStationCode = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                PreparationStationName = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_menu_item_views", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "product_views",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(
                    type: "character varying(60)",
                    maxLength: 60,
                    nullable: false
                ),
                Name = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                BasePrice = table.Column<decimal>(
                    type: "numeric(12,2)",
                    precision: 12,
                    scale: 2,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_views", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_category_views_Code",
            table: "category_views",
            column: "Code",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_menu_item_views_RestaurantId_ProductId",
            table: "menu_item_views",
            columns: new[] { "RestaurantId", "ProductId" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_product_views_Sku",
            table: "product_views",
            column: "Sku",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "category_views");

        migrationBuilder.DropTable(name: "inbox_messages");

        migrationBuilder.DropTable(name: "menu_item_views");

        migrationBuilder.DropTable(name: "product_views");
    }
}
