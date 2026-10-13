using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Dining.Infrastructure.Persistence;

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DiningDbContext))]
[Migration("20261013100000_AddDiningDiscounts")]
public sealed class AddDiningDiscounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DiscountId",
            table: "dining_sessions",
            type: "uuid",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "DiscountName",
            table: "dining_sessions",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true
        );
        migrationBuilder.AddColumn<decimal>(
            name: "DiscountPercentage",
            table: "dining_sessions",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.CreateTable(
            name: "dining_discounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false
                ),
                Percentage = table.Column<decimal>(
                    type: "numeric(5,2)",
                    precision: 5,
                    scale: 2,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table => table.PrimaryKey("PK_dining_discounts", x => x.Id)
        );

        migrationBuilder.CreateIndex(
            name: "IX_dining_discounts_RestaurantId_IsActive",
            table: "dining_discounts",
            columns: new[] { "RestaurantId", "IsActive" }
        );
        migrationBuilder.CreateIndex(
            name: "IX_dining_discounts_RestaurantId_Name",
            table: "dining_discounts",
            columns: new[] { "RestaurantId", "Name" },
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "dining_discounts");
        migrationBuilder.DropColumn(name: "DiscountId", table: "dining_sessions");
        migrationBuilder.DropColumn(name: "DiscountName", table: "dining_sessions");
        migrationBuilder.DropColumn(name: "DiscountPercentage", table: "dining_sessions");
    }
}
