using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveDiningPoliciesAndAddVirtualLocations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"dining_restaurant_policies\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "dining_restaurant_policies",
            columns: table => new
            {
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                AllowCheckoutBeforeKitchenCompletion = table.Column<bool>(
                    type: "boolean",
                    nullable: false,
                    defaultValue: false
                ),
                QrAllowedNetworks = table.Column<string>(
                    type: "character varying(2000)",
                    maxLength: 2000,
                    nullable: false
                ),
                QrRequiresImmediatePayment = table.Column<bool>(
                    type: "boolean",
                    nullable: false,
                    defaultValue: true
                ),
                RequireTrustedNetworkForQr = table.Column<bool>(type: "boolean", nullable: false),
                TakeawayRequiresPrepayment = table.Column<bool>(
                    type: "boolean",
                    nullable: false,
                    defaultValue: true
                ),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                Version = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dining_restaurant_policies", x => x.RestaurantId);
            }
        );
    }
}
