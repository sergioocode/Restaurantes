using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddKitchenStationDispatchConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPrimary",
            table: "kitchen_station_views",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<int>(
            name: "Priority",
            table: "kitchen_station_views",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<bool>(
            name: "RequiresPrimaryDispatch",
            table: "kitchen_station_views",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            """
            UPDATE kitchen_station_views
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
                SELECT DISTINCT "RestaurantId" FROM kitchen_station_views
            )
            INSERT INTO kitchen_station_views
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
                SELECT 1 FROM kitchen_station_views existing
                WHERE existing."RestaurantId" = r."RestaurantId"
                  AND existing."Code" = station.code
            );

            INSERT INTO category_views
                ("Id", "Code", "Name", "DefaultStationCode", "DefaultStationName",
                 "IsActive", "Version", "UpdatedAtUtc")
            SELECT '10000000-0000-4000-8000-000000000001'::uuid,
                   'ENTRANTES', 'Entrantes', 'ENTRANTES', 'Entrantes', TRUE, 1, now()
            WHERE NOT EXISTS (SELECT 1 FROM category_views WHERE "Code" = 'ENTRANTES');
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_kitchen_station_views_RestaurantId",
            table: "kitchen_station_views",
            column: "RestaurantId",
            unique: true,
            filter: "\"IsPrimary\" = TRUE AND \"IsActive\" = TRUE"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_kitchen_station_views_RestaurantId",
            table: "kitchen_station_views"
        );

        migrationBuilder.DropColumn(name: "IsPrimary", table: "kitchen_station_views");

        migrationBuilder.DropColumn(name: "Priority", table: "kitchen_station_views");

        migrationBuilder.DropColumn(
            name: "RequiresPrimaryDispatch",
            table: "kitchen_station_views"
        );
    }
}
