using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogDesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowInCommander",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "ShowInCustomerQr",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "ShowInPos",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE categories ALTER COLUMN "ShowInPos" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "ShowInCommander" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "ShowInCustomerQr" DROP DEFAULT;
                """
            );

            migrationBuilder.CreateTable(
                name: "allergens",
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
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergens", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "catalog_menus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_menus", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "product_configurations",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsCombo = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_configurations", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_product_configurations_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "product_option_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    MinSelections = table.Column<int>(type: "integer", nullable: false),
                    MaxSelections = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_option_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_option_groups_products_ParentProductId",
                        column: x => x.ParentProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "product_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Format = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Amount = table.Column<decimal>(
                        type: "numeric(12,2)",
                        precision: 12,
                        scale: 2,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_prices_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "taxes",
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
                    Rate = table.Column<decimal>(
                        type: "numeric(7,4)",
                        precision: 7,
                        scale: 4,
                        nullable: false
                    ),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_taxes", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "product_allergens",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllergenId = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_product_allergens",
                        x => new { x.ProductId, x.AllergenId }
                    );
                    table.ForeignKey(
                        name: "FK_product_allergens_allergens_AllergenId",
                        column: x => x.AllergenId,
                        principalTable: "allergens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_product_allergens_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "catalog_menu_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_menu_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalog_menu_entries_catalog_menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "catalog_menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_catalog_menu_entries_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "product_options",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_options", x => new { x.GroupId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_product_options_product_option_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "product_option_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_product_options_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "product_taxes",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxId = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_taxes", x => new { x.ProductId, x.TaxId });
                    table.ForeignKey(
                        name: "FK_product_taxes_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_product_taxes_taxes_TaxId",
                        column: x => x.TaxId,
                        principalTable: "taxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "catalog_menu_channels",
                columns: table => new
                {
                    MenuEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalId = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_catalog_menu_channels",
                        x => new { x.MenuEntryId, x.Channel }
                    );
                    table.ForeignKey(
                        name: "FK_catalog_menu_channels_catalog_menu_entries_MenuEntryId",
                        column: x => x.MenuEntryId,
                        principalTable: "catalog_menu_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_allergens_Code",
                table: "allergens",
                column: "Code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_catalog_menu_entries_MenuId_ProductId",
                table: "catalog_menu_entries",
                columns: new[] { "MenuId", "ProductId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_catalog_menu_entries_ProductId",
                table: "catalog_menu_entries",
                column: "ProductId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_catalog_menus_RestaurantId",
                table: "catalog_menus",
                column: "RestaurantId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_product_allergens_AllergenId",
                table: "product_allergens",
                column: "AllergenId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_product_option_groups_ParentProductId",
                table: "product_option_groups",
                column: "ParentProductId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_product_options_ProductId",
                table: "product_options",
                column: "ProductId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_ProductId_RestaurantId_Channel_Format",
                table: "product_prices",
                columns: new[] { "ProductId", "RestaurantId", "Channel", "Format" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_product_taxes_TaxId",
                table: "product_taxes",
                column: "TaxId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_taxes_Code",
                table: "taxes",
                column: "Code",
                unique: true
            );

            // Preserve the current local menus and their single legacy price as UNIDAD.
            // No product is added to the new global menu automatically.
            migrationBuilder.Sql(
                """
                INSERT INTO catalog_menus ("Id", "RestaurantId", "Name")
                VALUES ('11111111-1111-4111-8111-111111111111',
                        '00000000-0000-0000-0000-000000000000', 'Carta general');

                INSERT INTO catalog_menus ("Id", "RestaurantId", "Name")
                SELECT DISTINCT ON (r."RestaurantId") r."Id", r."RestaurantId", 'Carta local'
                FROM restaurant_menu_items AS r
                ORDER BY r."RestaurantId", r."Id";

                INSERT INTO catalog_menu_entries ("Id", "MenuId", "ProductId", "IsAvailable", "SortOrder")
                SELECT r."Id", m."Id", r."ProductId", r."IsAvailable", 0
                FROM restaurant_menu_items AS r
                JOIN catalog_menus AS m ON m."RestaurantId" = r."RestaurantId";

                INSERT INTO product_prices ("Id", "ProductId", "RestaurantId", "Channel", "Format", "Amount")
                SELECT p."Id", p."Id", '00000000-0000-0000-0000-000000000000',
                       'LOCAL', 'UNIDAD', p."BasePrice"
                FROM products AS p;

                INSERT INTO product_prices ("Id", "ProductId", "RestaurantId", "Channel", "Format", "Amount")
                SELECT r."Id", r."ProductId", r."RestaurantId", 'LOCAL', 'UNIDAD', r."Price"
                FROM restaurant_menu_items AS r;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "catalog_menu_channels");

            migrationBuilder.DropTable(name: "product_allergens");

            migrationBuilder.DropTable(name: "product_configurations");

            migrationBuilder.DropTable(name: "product_options");

            migrationBuilder.DropTable(name: "product_prices");

            migrationBuilder.DropTable(name: "product_taxes");

            migrationBuilder.DropTable(name: "catalog_menu_entries");

            migrationBuilder.DropTable(name: "allergens");

            migrationBuilder.DropTable(name: "product_option_groups");

            migrationBuilder.DropTable(name: "taxes");

            migrationBuilder.DropTable(name: "catalog_menus");

            migrationBuilder.DropColumn(name: "ShowInCommander", table: "categories");

            migrationBuilder.DropColumn(name: "ShowInCustomerQr", table: "categories");

            migrationBuilder.DropColumn(name: "ShowInPos", table: "categories");
        }
    }
}
