using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261008010000_UsePublishedCatalogMenus")]
public sealed class UsePublishedCatalogMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM product_taxes GROUP BY "ProductId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Hay productos con varios impuestos; asigna uno antes de migrar.';
                END IF;
            END $$;

            ALTER TABLE products ADD COLUMN "TaxId" uuid NULL;
            UPDATE products p SET "TaxId" = pt."TaxId"
            FROM product_taxes pt WHERE pt."ProductId" = p."Id";
            CREATE INDEX "IX_products_TaxId" ON products ("TaxId");
            ALTER TABLE products ADD CONSTRAINT "FK_products_taxes_TaxId"
                FOREIGN KEY ("TaxId") REFERENCES taxes ("Id") ON DELETE RESTRICT;
            DROP TABLE product_taxes;

            ALTER TABLE product_configurations ADD COLUMN "AllowsExtras" boolean NOT NULL DEFAULT FALSE;
            UPDATE product_configurations c SET "AllowsExtras" = TRUE
            WHERE c."IsCombo" = TRUE AND EXISTS (
                SELECT 1 FROM product_option_groups g
                WHERE g."ParentProductId" = c."ProductId" AND g."Kind" = 'Extra'
            );

            DROP INDEX IF EXISTS "IX_catalog_menus_RestaurantId";
            ALTER TABLE catalog_menus ADD COLUMN "IsActive" boolean NOT NULL DEFAULT FALSE;
            CREATE UNIQUE INDEX "IX_catalog_menus_RestaurantId_IsActive"
                ON catalog_menus ("RestaurantId", "IsActive") WHERE "IsActive" = TRUE;

            ALTER TABLE catalog_menu_entries ADD COLUMN "DefaultFormat" character varying(20)
                NOT NULL DEFAULT 'PLATO';

            CREATE TABLE catalog_menu_prices (
                "MenuId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "Channel" character varying(20) NOT NULL,
                "Format" character varying(20) NOT NULL,
                "Amount" numeric(12,2) NOT NULL,
                CONSTRAINT "PK_catalog_menu_prices" PRIMARY KEY ("MenuId", "ProductId", "Channel", "Format"),
                CONSTRAINT "FK_catalog_menu_prices_catalog_menus_MenuId" FOREIGN KEY ("MenuId")
                    REFERENCES catalog_menus ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_catalog_menu_prices_products_ProductId" FOREIGN KEY ("ProductId")
                    REFERENCES products ("Id") ON DELETE RESTRICT
            );
            CREATE INDEX "IX_catalog_menu_prices_ProductId" ON catalog_menu_prices ("ProductId");

            CREATE TABLE product_photos (
                "ProductId" uuid NOT NULL,
                "Revision" uuid NOT NULL,
                "OriginalContentType" character varying(40) NOT NULL,
                CONSTRAINT "PK_product_photos" PRIMARY KEY ("ProductId"),
                CONSTRAINT "FK_product_photos_products_ProductId" FOREIGN KEY ("ProductId")
                    REFERENCES products ("Id") ON DELETE CASCADE
            );

            DELETE FROM product_prices WHERE "Id" IN
                (SELECT "Id" FROM products UNION SELECT "Id" FROM restaurant_menu_items);

            INSERT INTO catalog_menu_prices ("MenuId", "ProductId", "Channel", "Format", "Amount")
            SELECT m."Id", p."ProductId", p."Channel", p."Format", p."Amount"
            FROM catalog_menus m CROSS JOIN product_prices p
            WHERE m."RestaurantId" = '00000000-0000-0000-0000-000000000000'
              AND p."RestaurantId" = '00000000-0000-0000-0000-000000000000';

            INSERT INTO catalog_menu_entries ("Id", "MenuId", "ProductId", "IsAvailable", "DefaultFormat", "SortOrder")
            SELECT gen_random_uuid(), m."Id", p."ProductId", TRUE, p."Format", 0
            FROM catalog_menus m
            CROSS JOIN LATERAL (
                SELECT DISTINCT ON ("ProductId") "ProductId", "Format"
                FROM product_prices
                WHERE "RestaurantId" = '00000000-0000-0000-0000-000000000000' AND "Channel" = 'LOCAL'
                ORDER BY "ProductId", CASE WHEN "Format" = 'PLATO' THEN 0
                    WHEN "Format" = 'EXTRA' THEN 1 ELSE 2 END, "Format"
            ) p
            WHERE m."RestaurantId" = '00000000-0000-0000-0000-000000000000'
              AND NOT EXISTS (SELECT 1 FROM catalog_menu_entries e
                  WHERE e."MenuId" = m."Id" AND e."ProductId" = p."ProductId");
            ALTER TABLE products DROP COLUMN "BasePrice";
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE products ADD COLUMN "BasePrice" numeric(12,2) NOT NULL DEFAULT 0;
            DROP TABLE product_photos;
            DROP TABLE catalog_menu_prices;
            ALTER TABLE product_configurations DROP COLUMN "AllowsExtras";
            ALTER TABLE catalog_menu_entries DROP COLUMN "DefaultFormat";
            DROP INDEX "IX_catalog_menus_RestaurantId_IsActive";
            ALTER TABLE catalog_menus DROP COLUMN "IsActive";
            CREATE UNIQUE INDEX "IX_catalog_menus_RestaurantId" ON catalog_menus ("RestaurantId");
            CREATE TABLE product_taxes (
                "ProductId" uuid NOT NULL,
                "TaxId" uuid NOT NULL,
                CONSTRAINT "PK_product_taxes" PRIMARY KEY ("ProductId", "TaxId"),
                CONSTRAINT "FK_product_taxes_products_ProductId" FOREIGN KEY ("ProductId")
                    REFERENCES products ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_product_taxes_taxes_TaxId" FOREIGN KEY ("TaxId")
                    REFERENCES taxes ("Id") ON DELETE RESTRICT
            );
            INSERT INTO product_taxes ("ProductId", "TaxId")
                SELECT "Id", "TaxId" FROM products WHERE "TaxId" IS NOT NULL;
            CREATE INDEX "IX_product_taxes_TaxId" ON product_taxes ("TaxId");
            ALTER TABLE products DROP CONSTRAINT "FK_products_taxes_TaxId";
            DROP INDEX "IX_products_TaxId";
            ALTER TABLE products DROP COLUMN "TaxId";
            """
        );
    }
}
