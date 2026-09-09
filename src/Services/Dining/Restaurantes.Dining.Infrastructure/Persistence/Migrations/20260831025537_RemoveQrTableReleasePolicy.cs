using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveQrTableReleasePolicy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReleaseQrTableOnFullPayment",
            table: "dining_restaurant_policies"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ReleaseQrTableOnFullPayment",
            table: "dining_restaurant_policies",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );
    }
}
