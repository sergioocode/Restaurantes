using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Identity.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExplicitRestaurantScope : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AllRestaurants",
            table: "authorized_accounts",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            "UPDATE authorized_accounts SET \"AllRestaurants\" = TRUE WHERE \"RestaurantId\" IS NULL;"
        );

        migrationBuilder.AddCheckConstraint(
            name: "CK_authorized_accounts_restaurant_scope",
            table: "authorized_accounts",
            sql: "(\"AllRestaurants\" AND \"RestaurantId\" IS NULL) OR (NOT \"AllRestaurants\" AND \"RestaurantId\" IS NOT NULL)"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_authorized_accounts_restaurant_scope",
            table: "authorized_accounts"
        );

        migrationBuilder.DropColumn(name: "AllRestaurants", table: "authorized_accounts");
    }
}
