using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddKitchenStationDispatchConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPrimary",
            table: "restaurant_kitchen_stations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<int>(
            name: "Priority",
            table: "restaurant_kitchen_stations",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<bool>(
            name: "RequiresPrimaryDispatch",
            table: "restaurant_kitchen_stations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            """
            UPDATE restaurant_kitchen_stations
            SET "IsPrimary" = ("Code" = 'CHEF'),
                "RequiresPrimaryDispatch" = ("Code" IN ('ENTRANTES', 'PASTAS', 'CARNES')),
                "Priority" = CASE
                    WHEN "Code" = 'CHEF' THEN 0
                    WHEN "Code" IN ('ENTRANTES', 'BEBIDAS') THEN 1
                    WHEN "Code" IN ('PASTAS', 'CARNES') THEN 2
                    WHEN "Code" = 'POSTRES' THEN 3
                    ELSE 4
                END;

            WITH restaurants AS (
                SELECT DISTINCT "RestaurantId" FROM restaurant_kitchen_stations
            )
            INSERT INTO restaurant_kitchen_stations
                ("Id", "RestaurantId", "Code", "Name", "IsPrimary",
                 "RequiresPrimaryDispatch", "Priority", "IsActive", "Version", "UpdatedAtUtc")
            SELECT md5(r."RestaurantId"::text || ':' || station.code)::uuid,
                   r."RestaurantId", station.code, station.name, station.is_primary,
                   station.requires_dispatch, station.priority, TRUE, 1, now()
            FROM restaurants r
            CROSS JOIN (
                VALUES
                    ('CHEF', 'Vista completa', TRUE, FALSE, 0),
                    ('ENTRANTES', 'Entrantes', FALSE, TRUE, 1)
            ) AS station(code, name, is_primary, requires_dispatch, priority)
            WHERE NOT EXISTS (
                SELECT 1 FROM restaurant_kitchen_stations existing
                WHERE existing."RestaurantId" = r."RestaurantId"
                  AND existing."Code" = station.code
            );

            INSERT INTO categories
                ("Id", "Code", "Name", "DefaultStationCode", "DefaultStationName",
                 "IsActive", "Version", "UpdatedAtUtc")
            SELECT '10000000-0000-4000-8000-000000000001'::uuid,
                   'ENTRANTES', 'Entrantes', 'ENTRANTES', 'Entrantes', TRUE, 1, now()
            WHERE NOT EXISTS (SELECT 1 FROM categories WHERE "Code" = 'ENTRANTES');

            INSERT INTO outbox_messages
                ("Id", "Type", "Payload", "OccurredAtUtc", "ProcessedAtUtc", "Error")
            SELECT gen_random_uuid(),
                   'Restaurantes.Catalog.Contracts.KitchenStationChanged',
                   jsonb_build_object(
                       'StationId', "Id", 'RestaurantId', "RestaurantId",
                       'Code', "Code", 'Name', "Name", 'IsPrimary', "IsPrimary",
                       'RequiresPrimaryDispatch', "RequiresPrimaryDispatch",
                       'Priority', "Priority", 'IsActive', "IsActive",
                       'Version', "Version", 'OccurredAtUtc', "UpdatedAtUtc"
                   ),
                   now(), NULL, NULL
            FROM restaurant_kitchen_stations;

            INSERT INTO outbox_messages
                ("Id", "Type", "Payload", "OccurredAtUtc", "ProcessedAtUtc", "Error")
            SELECT gen_random_uuid(),
                   'Restaurantes.Catalog.Contracts.CategoryChanged',
                   jsonb_build_object(
                       'CategoryId', "Id", 'Code', "Code", 'Name', "Name",
                       'DefaultStationCode', "DefaultStationCode",
                       'DefaultStationName', "DefaultStationName",
                       'IsActive', "IsActive", 'Version', "Version",
                       'OccurredAtUtc', "UpdatedAtUtc"
                   ),
                   now(), NULL, NULL
            FROM categories
            WHERE "Code" = 'ENTRANTES';
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_restaurant_kitchen_stations_RestaurantId",
            table: "restaurant_kitchen_stations",
            column: "RestaurantId",
            unique: true,
            filter: "\"IsPrimary\" = TRUE AND \"IsActive\" = TRUE"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_restaurant_kitchen_stations_RestaurantId",
            table: "restaurant_kitchen_stations"
        );

        migrationBuilder.DropColumn(name: "IsPrimary", table: "restaurant_kitchen_stations");

        migrationBuilder.DropColumn(name: "Priority", table: "restaurant_kitchen_stations");

        migrationBuilder.DropColumn(
            name: "RequiresPrimaryDispatch",
            table: "restaurant_kitchen_stations"
        );
    }
}
