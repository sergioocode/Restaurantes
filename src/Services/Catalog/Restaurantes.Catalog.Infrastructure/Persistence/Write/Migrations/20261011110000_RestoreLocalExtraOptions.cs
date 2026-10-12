using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(CatalogWriteDbContext))]
[Migration("20261011110000_RestoreLocalExtraOptions")]
public sealed class RestoreLocalExtraOptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            WITH changed AS (
                UPDATE restaurant_menu_items item
                SET "Version" = item."Version" + 1, "UpdatedAtUtc" = now()
                WHERE EXISTS (
                    SELECT 1 FROM product_option_groups group_item
                    WHERE group_item."ParentProductId" = item."ProductId"
                      AND group_item."Kind" = 'Extra'
                )
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
                       'DefaultFormat', COALESCE(entry."DefaultFormat", 'PLATO'),
                       'Formats', COALESCE((
                           SELECT jsonb_agg(jsonb_build_object('Format', price."Format", 'Price', price."Amount")
                                            ORDER BY price."Format")
                           FROM catalog_menu_prices price
                           WHERE price."MenuId" = menu."Id"
                             AND price."ProductId" = changed."ProductId"
                             AND price."Channel" = 'LOCAL'
                       ), '[]'::jsonb),
                       'ExtraGroups', COALESCE((
                           SELECT jsonb_agg(jsonb_build_object(
                               'Id', group_item."Id", 'Name', group_item."Name",
                               'MinSelections', group_item."MinSelections",
                               'MaxSelections', group_item."MaxSelections",
                               'Options', selected.options
                           ) ORDER BY group_item."SortOrder")
                           FROM product_option_groups group_item
                           CROSS JOIN LATERAL (
                               SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                   'ProductId', extra."Id", 'Sku', extra."Sku",
                                   'ProductName', extra."Name", 'Price', extra_price."Amount"
                               ) ORDER BY option_item."SortOrder"), '[]'::jsonb) AS options
                               FROM product_options option_item
                               JOIN products extra ON extra."Id" = option_item."ProductId"
                               JOIN taxes extra_tax ON extra_tax."Id" = extra."TaxId"
                                   AND extra_tax."IsActive"
                               JOIN catalog_menu_prices extra_price
                                   ON extra_price."MenuId" = menu."Id"
                                  AND extra_price."ProductId" = extra."Id"
                                  AND extra_price."Channel" = 'LOCAL'
                                  AND extra_price."Format" = 'RACION'
                               WHERE option_item."GroupId" = group_item."Id"
                                 AND extra."IsActive"
                           ) selected
                           WHERE group_item."ParentProductId" = changed."ProductId"
                             AND group_item."Kind" = 'Extra'
                             AND jsonb_array_length(selected.options) > 0
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
