using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddOrderLineCancellation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CancellationReason",
            table: "order_lines",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "CancelledAtUtc",
            table: "order_lines",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "Status",
            table: "order_lines",
            type: "integer",
            nullable: false,
            defaultValue: 1
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CancellationReason", table: "order_lines");

        migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "order_lines");

        migrationBuilder.DropColumn(name: "Status", table: "order_lines");
    }
}
