using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddPaymentDetailsToOrderPaymentProjection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "CapturedAtUtc",
            table: "order_payments",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "Method",
            table: "order_payments",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true
        );

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('payments.payable_orders') IS NOT NULL THEN
                    UPDATE orders_write.order_payments AS projection
                    SET "Method" = payment."Method", "CapturedAtUtc" = payment."CapturedAtUtc"
                    FROM payments.payable_orders AS payment
                    WHERE projection."OrderId" = payment."OrderId"
                      AND payment."CapturedAtUtc" IS NOT NULL;
                END IF;
            END $$;
            """
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CapturedAtUtc", table: "order_payments");

        migrationBuilder.DropColumn(name: "Method", table: "order_payments");
    }
}
