using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class LinkOrdersToCashSession : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "BusinessDate",
            table: "orders",
            type: "date",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "CashRegisterSessionId",
            table: "orders",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.AddColumn<bool>(
            name: "CanViewHistory",
            table: "kitchen_station_configurations",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            "UPDATE orders_write.kitchen_station_configurations SET \"CanViewHistory\" = TRUE WHERE \"IsPrimary\" = TRUE;"
        );
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('cash_register.cash_register_sessions') IS NOT NULL THEN
                    UPDATE orders_write.orders AS o
                    SET "CashRegisterSessionId" = s."Id", "BusinessDate" = s."BusinessDate"
                    FROM cash_register.cash_register_sessions AS s
                    WHERE o."RestaurantId" = s."RestaurantId"
                      AND o."CreatedAtUtc" >= s."OpenedAtUtc"
                      AND (s."ClosedAtUtc" IS NULL OR o."CreatedAtUtc" <= s."ClosedAtUtc");
                END IF;
            END $$;
            """
        );
        migrationBuilder.CreateIndex(
            name: "IX_orders_RestaurantId_BusinessDate_CashRegisterSessionId",
            table: "orders",
            columns: new[] { "RestaurantId", "BusinessDate", "CashRegisterSessionId" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_orders_RestaurantId_BusinessDate_CashRegisterSessionId",
            table: "orders"
        );

        migrationBuilder.DropColumn(name: "BusinessDate", table: "orders");

        migrationBuilder.DropColumn(name: "CashRegisterSessionId", table: "orders");

        migrationBuilder.DropColumn(
            name: "CanViewHistory",
            table: "kitchen_station_configurations"
        );
    }
}
