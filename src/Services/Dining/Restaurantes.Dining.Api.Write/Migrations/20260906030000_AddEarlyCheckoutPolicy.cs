using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Api.Write.Migrations;

[DbContext(typeof(DiningDbContext))]
[Migration("20260906030000_AddEarlyCheckoutPolicy")]
public sealed class AddEarlyCheckoutPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AllowCheckoutBeforeKitchenCompletion",
            table: "dining_restaurant_policies",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AllowCheckoutBeforeKitchenCompletion",
            table: "dining_restaurant_policies"
        );
    }
}
