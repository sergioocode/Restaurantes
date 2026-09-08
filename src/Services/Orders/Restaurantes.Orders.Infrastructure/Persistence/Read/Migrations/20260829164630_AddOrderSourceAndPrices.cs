using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddOrderSourceAndPrices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PaymentTiming",
            table: "kitchen_order_views",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "OnAccount"
        );

        migrationBuilder.AddColumn<string>(
            name: "Source",
            table: "kitchen_order_views",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "Pos"
        );

        migrationBuilder.AddColumn<decimal>(
            name: "Total",
            table: "kitchen_order_views",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PaymentTiming", table: "kitchen_order_views");

        migrationBuilder.DropColumn(name: "Source", table: "kitchen_order_views");

        migrationBuilder.DropColumn(name: "Total", table: "kitchen_order_views");
    }
}
