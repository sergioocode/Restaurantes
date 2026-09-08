using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Api.Write.Migrations;

/// <inheritdoc />
public partial class AddDiningSessionCancellation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CancellationReason",
            table: "dining_sessions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "CancelledAtUtc",
            table: "dining_sessions",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "CancelledByUserId",
            table: "dining_sessions",
            type: "uuid",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CancellationReason", table: "dining_sessions");

        migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "dining_sessions");

        migrationBuilder.DropColumn(name: "CancelledByUserId", table: "dining_sessions");
    }
}
