using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Api.Write.Migrations;

/// <inheritdoc />
public partial class AddQrImmediatePaymentPolicy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "QrRequiresImmediatePayment",
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
            name: "QrRequiresImmediatePayment",
            table: "dining_restaurant_policies"
        );
    }
}
