using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261010100000_LocalProductAvailabilityOnly")]
public sealed class LocalProductAvailabilityOnly : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            WITH changed AS (
                UPDATE products
                SET "IsActive" = TRUE, "Version" = "Version" + 1,
                    "UpdatedAtUtc" = now()
                WHERE NOT "IsActive"
                RETURNING *
            )
            INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
            SELECT gen_random_uuid(), 'Restaurantes.Catalog.Contracts.Events.ProductChanged',
                   jsonb_build_object(
                       'ProductId', changed."Id", 'Sku', changed."Sku",
                       'Name', changed."Name", 'CategoryId', changed."CategoryId",
                       'IsActive', TRUE, 'Version', changed."Version",
                       'OccurredAtUtc', changed."UpdatedAtUtc"
                   ), changed."UpdatedAtUtc"
            FROM changed;

            UPDATE catalog_menu_entries entry
            SET "IsAvailable" = TRUE
            FROM catalog_menus menu
            WHERE entry."MenuId" = menu."Id"
              AND menu."RestaurantId" = '00000000-0000-0000-0000-000000000000'
              AND NOT entry."IsAvailable";

            WITH desired AS (
                SELECT item."Id", COALESCE(price."Amount", item."Price") AS price,
                       category."IsActive" AND COALESCE(tax."IsActive", FALSE)
                       AND COALESCE(selection."IsAvailable", FALSE)
                       AND price."Amount" IS NOT NULL AS available
                FROM restaurant_menu_items item
                JOIN products product ON product."Id" = item."ProductId"
                JOIN categories category ON category."Id" = product."CategoryId"
                JOIN catalog_menus menu ON menu."RestaurantId" = '00000000-0000-0000-0000-000000000000'
                    AND menu."IsActive"
                LEFT JOIN taxes tax ON tax."Id" = product."TaxId"
                LEFT JOIN catalog_menu_entries entry ON entry."MenuId" = menu."Id"
                    AND entry."ProductId" = item."ProductId"
                LEFT JOIN catalog_menu_prices price ON price."MenuId" = menu."Id"
                    AND price."ProductId" = item."ProductId" AND price."Channel" = 'LOCAL'
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
                           SELECT old."Payload" -> 'ExtraGroups' FROM outbox_messages old
                           WHERE old."Type" = 'Restaurantes.Catalog.Contracts.Events.CatalogItemChanged'
                             AND old."Payload" ->> 'RestaurantId' = changed."RestaurantId"::text
                             AND old."Payload" ->> 'ProductId' = changed."ProductId"::text
                           ORDER BY old."OccurredAtUtc" DESC LIMIT 1
                       ), '[]'::jsonb)
                   ), changed."UpdatedAtUtc"
            FROM changed JOIN products product ON product."Id" = changed."ProductId"
            JOIN categories category ON category."Id" = product."CategoryId";
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Los estados globales anteriores no pueden reconstruirse tras normalizarlos.
    }
}
