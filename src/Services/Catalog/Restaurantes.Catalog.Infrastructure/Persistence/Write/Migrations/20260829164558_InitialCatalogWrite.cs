using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class InitialCatalogWrite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "categories",
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
                table.PrimaryKey("PK_categories", x => x.Id);
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
            name: "products",
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
                table.PrimaryKey("PK_products", x => x.Id);
                table.ForeignKey(
                    name: "FK_products_categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "restaurant_menu_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_restaurant_menu_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_restaurant_menu_items_products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_categories_Code",
            table: "categories",
            column: "Code",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedAtUtc_OccurredAtUtc",
            table: "outbox_messages",
            columns: new[] { "ProcessedAtUtc", "OccurredAtUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_products_CategoryId",
            table: "products",
            column: "CategoryId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_products_Sku",
            table: "products",
            column: "Sku",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_restaurant_menu_items_ProductId",
            table: "restaurant_menu_items",
            column: "ProductId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_restaurant_menu_items_RestaurantId_ProductId",
            table: "restaurant_menu_items",
            columns: new[] { "RestaurantId", "ProductId" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages");

        migrationBuilder.DropTable(name: "restaurant_menu_items");

        migrationBuilder.DropTable(name: "products");

        migrationBuilder.DropTable(name: "categories");
    }
}
