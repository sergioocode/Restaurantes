using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Api.Write.Migrations;

/// <inheritdoc />
public partial class AddSessionCheckout : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CheckoutIdempotencyKey",
            table: "dining_sessions",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "PaidAtUtc",
            table: "dining_sessions",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "PaymentMethod",
            table: "dining_sessions",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<decimal>(
            name: "Amount",
            table: "dining_session_orders",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );

        migrationBuilder.AddColumn<string>(
            name: "OrderStatus",
            table: "dining_session_orders",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: ""
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CheckoutIdempotencyKey", table: "dining_sessions");

        migrationBuilder.DropColumn(name: "PaidAtUtc", table: "dining_sessions");

        migrationBuilder.DropColumn(name: "PaymentMethod", table: "dining_sessions");

        migrationBuilder.DropColumn(name: "Amount", table: "dining_session_orders");

        migrationBuilder.DropColumn(name: "OrderStatus", table: "dining_session_orders");
    }
}
