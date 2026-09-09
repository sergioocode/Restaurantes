using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTakeawayPaymentPolicy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "TakeawayRequiresPrepayment",
            table: "dining_restaurant_policies",
            type: "boolean",
            nullable: false,
            defaultValue: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TakeawayRequiresPrepayment",
            table: "dining_restaurant_policies"
        );
    }
}
