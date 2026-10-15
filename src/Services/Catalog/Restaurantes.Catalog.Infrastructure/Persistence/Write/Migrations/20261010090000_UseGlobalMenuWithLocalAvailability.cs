using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261010090000_UseGlobalMenuWithLocalAvailability")]
public sealed class UseGlobalMenuWithLocalAvailability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE restaurant_product_availability (
                "RestaurantId" uuid NOT NULL,
                "ProductId" uuid NOT NULL REFERENCES products ("Id") ON DELETE CASCADE,
                "IsAvailable" boolean NOT NULL,
                CONSTRAINT "PK_restaurant_product_availability" PRIMARY KEY ("RestaurantId", "ProductId")
            );
            CREATE INDEX "IX_restaurant_product_availability_ProductId"
                ON restaurant_product_availability ("ProductId");

            -- Preserve each local's current selection. A missing product in an old local
            -- menu was unavailable there, even though it exists in the global menu.
            INSERT INTO restaurant_product_availability ("RestaurantId", "ProductId", "IsAvailable")
            SELECT local_menu."RestaurantId", global_entry."ProductId",
                   COALESCE(local_entry."IsAvailable", FALSE)
            FROM catalog_menus local_menu
            JOIN catalog_menus global_menu ON global_menu."RestaurantId" = '00000000-0000-0000-0000-000000000000'
            JOIN catalog_menu_entries global_entry ON global_entry."MenuId" = global_menu."Id"
            LEFT JOIN catalog_menu_entries local_entry ON local_entry."MenuId" = local_menu."Id"
                AND local_entry."ProductId" = global_entry."ProductId"
            WHERE local_menu."RestaurantId" <> '00000000-0000-0000-0000-000000000000'
              AND local_menu."IsActive";

            DELETE FROM catalog_menus WHERE "RestaurantId" <> '00000000-0000-0000-0000-000000000000';
            DROP INDEX IF EXISTS "IX_catalog_menus_RestaurantId_IsActive";
            CREATE UNIQUE INDEX "IX_catalog_menus_RestaurantId" ON catalog_menus ("RestaurantId");
            ALTER TABLE catalog_menus ADD CONSTRAINT "CK_catalog_menus_global_only"
                CHECK ("RestaurantId" = '00000000-0000-0000-0000-000000000000');

            WITH desired AS (
                SELECT item."Id", COALESCE(price."Amount", item."Price") AS price,
                       product."IsActive" AND category."IsActive"
                       AND COALESCE(tax."IsActive", FALSE) AND entry."IsAvailable"
                       AND COALESCE(selection."IsAvailable", TRUE)
                       AND price."Amount" IS NOT NULL AS available
                FROM restaurant_menu_items item
                JOIN catalog_menu_entries entry ON entry."ProductId" = item."ProductId"
                JOIN catalog_menus menu ON menu."Id" = entry."MenuId" AND menu."IsActive"
                JOIN products product ON product."Id" = item."ProductId"
                JOIN categories category ON category."Id" = product."CategoryId"
                LEFT JOIN taxes tax ON tax."Id" = product."TaxId"
                LEFT JOIN catalog_menu_prices price ON price."MenuId" = menu."Id"
                    AND price."ProductId" = entry."ProductId" AND price."Channel" = 'LOCAL'
                    AND price."Format" = entry."DefaultFormat"
                LEFT JOIN restaurant_product_availability selection
                    ON selection."RestaurantId" = item."RestaurantId"
                    AND selection."ProductId" = item."ProductId"
            ), changed AS (
                UPDATE restaurant_menu_items item
                SET "Price" = desired.price, "IsAvailable" = desired.available,
                    "Version" = item."Version" + 1, "UpdatedAtUtc" = now()
                FROM desired WHERE item."Id" = desired."Id"
                    AND (item."Price" IS DISTINCT FROM desired.price
                         OR item."IsAvailable" IS DISTINCT FROM desired.available)
                RETURNING item.*
            )
            INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
            SELECT gen_random_uuid(), 'Restaurantes.Catalog.Contracts.Events.CatalogItemChanged',
                   jsonb_build_object(
                       'RestaurantId', changed."RestaurantId", 'ProductId', changed."ProductId",
                       'Sku', product."Sku", 'ProductName', product."Name",
                       'CategoryId', category."Id", 'CategoryCode', category."Code",
                       'CategoryName', category."Name", 'Price', changed."Price",
                       'IsAvailable', changed."IsAvailable",
                       'PreparationStationCode', changed."PreparationStationCode",
                       'PreparationStationName', changed."PreparationStationName",
                       'Version', changed."Version", 'OccurredAtUtc', changed."UpdatedAtUtc",
                       'ExtraGroups', COALESCE((
                           SELECT jsonb_agg(jsonb_set(group_item.value, '{Options}', selected.options))
                           FROM jsonb_array_elements(COALESCE((
                               SELECT old."Payload" -> 'ExtraGroups' FROM outbox_messages old
                               WHERE old."Type" = 'Restaurantes.Catalog.Contracts.Events.CatalogItemChanged'
                                 AND old."Payload" ->> 'RestaurantId' = changed."RestaurantId"::text
                                 AND old."Payload" ->> 'ProductId' = changed."ProductId"::text
                               ORDER BY old."OccurredAtUtc" DESC LIMIT 1
                           ), '[]'::jsonb)) AS group_item(value)
                           CROSS JOIN LATERAL (
                               SELECT COALESCE(jsonb_agg(option_item.value), '[]'::jsonb) AS options
                               FROM jsonb_array_elements(group_item.value -> 'Options') AS option_item(value)
                               LEFT JOIN restaurant_product_availability option_selection
                                   ON option_selection."RestaurantId" = changed."RestaurantId"
                                   AND option_selection."ProductId" = (option_item.value ->> 'ProductId')::uuid
                               WHERE COALESCE(option_selection."IsAvailable", TRUE)
                           ) selected
                           WHERE jsonb_array_length(selected.options) > 0
                       ), '[]'::jsonb)), changed."UpdatedAtUtc"
            FROM changed JOIN products product ON product."Id" = changed."ProductId"
            JOIN categories category ON category."Id" = product."CategoryId";
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE catalog_menus DROP CONSTRAINT "CK_catalog_menus_global_only";
            DROP INDEX "IX_catalog_menus_RestaurantId";
            CREATE UNIQUE INDEX "IX_catalog_menus_RestaurantId_IsActive"
                ON catalog_menus ("RestaurantId", "IsActive") WHERE "IsActive" = TRUE;

            INSERT INTO catalog_menus ("Id", "RestaurantId", "Name", "IsActive")
            SELECT gen_random_uuid(), local."RestaurantId", 'Carta del local', TRUE
            FROM (SELECT DISTINCT "RestaurantId" FROM restaurant_menu_items) local;

            INSERT INTO catalog_menu_entries ("Id", "MenuId", "ProductId", "IsAvailable", "DefaultFormat", "SortOrder")
            SELECT gen_random_uuid(), local_menu."Id", global_entry."ProductId",
                   global_entry."IsAvailable" AND COALESCE(selection."IsAvailable", TRUE),
                   global_entry."DefaultFormat", global_entry."SortOrder"
            FROM catalog_menus local_menu
            JOIN catalog_menus global_menu ON global_menu."RestaurantId" = '00000000-0000-0000-0000-000000000000'
            JOIN catalog_menu_entries global_entry ON global_entry."MenuId" = global_menu."Id"
            LEFT JOIN restaurant_product_availability selection
                ON selection."RestaurantId" = local_menu."RestaurantId"
                AND selection."ProductId" = global_entry."ProductId"
            WHERE local_menu."RestaurantId" <> '00000000-0000-0000-0000-000000000000';

            INSERT INTO catalog_menu_prices ("MenuId", "ProductId", "Channel", "Format", "Amount")
            SELECT local_menu."Id", price."ProductId", price."Channel", price."Format", price."Amount"
            FROM catalog_menus local_menu
            JOIN catalog_menus global_menu ON global_menu."RestaurantId" = '00000000-0000-0000-0000-000000000000'
            JOIN catalog_menu_prices price ON price."MenuId" = global_menu."Id"
            WHERE local_menu."RestaurantId" <> '00000000-0000-0000-0000-000000000000';
            DROP TABLE restaurant_product_availability;
            """
        );
    }
}
