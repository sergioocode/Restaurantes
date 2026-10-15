using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261011100000_RepublishCatalogFormats")]
public sealed class RepublishCatalogFormats : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            WITH changed AS (
                UPDATE restaurant_menu_items item
                SET "Version" = item."Version" + 1, "UpdatedAtUtc" = now()
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
                       ), '[]'::jsonb),
                       'DefaultFormat', COALESCE(entry."DefaultFormat", 'PLATO'),
                       'Formats', COALESCE((
                           SELECT jsonb_agg(jsonb_build_object('Format', price."Format", 'Price', price."Amount")
                                            ORDER BY price."Format")
                           FROM catalog_menu_prices price
                           WHERE price."MenuId" = menu."Id"
                             AND price."ProductId" = changed."ProductId"
                             AND price."Channel" = 'LOCAL'
                       ), '[]'::jsonb)
                   ), changed."UpdatedAtUtc"
            FROM changed
            JOIN products product ON product."Id" = changed."ProductId"
            JOIN categories category ON category."Id" = product."CategoryId"
            LEFT JOIN catalog_menus menu ON menu."RestaurantId" = '00000000-0000-0000-0000-000000000000'
                AND menu."IsActive"
            LEFT JOIN catalog_menu_entries entry ON entry."MenuId" = menu."Id"
                AND entry."ProductId" = changed."ProductId";
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Los eventos publicados y las versiones de catálogo no se revierten.
    }
}
