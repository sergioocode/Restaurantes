using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddKitchenDispatchLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DispatchedAtUtc",
            table: "order_kitchen_stations",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "Priority",
            table: "order_kitchen_stations",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<bool>(
            name: "RequiresPrimaryDispatch",
            table: "order_kitchen_stations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            """
            UPDATE order_kitchen_stations
            SET "RequiresPrimaryDispatch" = ("Code" IN ('ENTRANTES', 'PASTAS', 'CARNES')),
                "Priority" = CASE
                    WHEN "Code" IN ('ENTRANTES', 'BEBIDAS') THEN 1
                    WHEN "Code" IN ('PASTAS', 'CARNES') THEN 2
                    WHEN "Code" = 'POSTRES' THEN 3
                    ELSE 4
                END;
            """
        );

        migrationBuilder.CreateTable(
            name: "kitchen_station_configurations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                Name = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                RequiresPrimaryDispatch = table.Column<bool>(type: "boolean", nullable: false),
                Priority = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kitchen_station_configurations", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_kitchen_station_configurations_RestaurantId_Code",
            table: "kitchen_station_configurations",
            columns: new[] { "RestaurantId", "Code" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "kitchen_station_configurations");

        migrationBuilder.DropColumn(name: "DispatchedAtUtc", table: "order_kitchen_stations");

        migrationBuilder.DropColumn(name: "Priority", table: "order_kitchen_stations");

        migrationBuilder.DropColumn(
            name: "RequiresPrimaryDispatch",
            table: "order_kitchen_stations"
        );
    }
}
