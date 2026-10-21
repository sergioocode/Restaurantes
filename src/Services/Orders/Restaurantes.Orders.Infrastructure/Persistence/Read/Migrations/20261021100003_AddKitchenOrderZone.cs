using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Orders.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(OrderReadDbContext))]
[Migration("20261021100003_AddKitchenOrderZone")]
public sealed class AddKitchenOrderZone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>(
            name: "ZoneId",
            table: "kitchen_order_views",
            type: "uuid",
            nullable: true
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "ZoneId", table: "kitchen_order_views");
}
