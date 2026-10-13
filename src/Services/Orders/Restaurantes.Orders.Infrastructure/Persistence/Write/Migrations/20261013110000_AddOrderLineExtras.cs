using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

[DbContext(typeof(OrderWriteDbContext))]
[Migration("20261013110000_AddOrderLineExtras")]
public sealed class AddOrderLineExtras : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "BaseUnitPrice",
            table: "order_lines",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<string>(
            name: "ExtrasJson",
            table: "order_lines",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb"
        );
        migrationBuilder.Sql("UPDATE order_lines SET \"BaseUnitPrice\" = \"UnitPrice\";");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BaseUnitPrice", table: "order_lines");
        migrationBuilder.DropColumn(name: "ExtrasJson", table: "order_lines");
    }
}
