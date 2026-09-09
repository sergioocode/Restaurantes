using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddQrTrustedNetworks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "QrAllowedNetworks",
            table: "dining_restaurant_policies",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<bool>(
            name: "RequireTrustedNetworkForQr",
            table: "dining_restaurant_policies",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "QrAllowedNetworks", table: "dining_restaurant_policies");

        migrationBuilder.DropColumn(
            name: "RequireTrustedNetworkForQr",
            table: "dining_restaurant_policies"
        );
    }
}
