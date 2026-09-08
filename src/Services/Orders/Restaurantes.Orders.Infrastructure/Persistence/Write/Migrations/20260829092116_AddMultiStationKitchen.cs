using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Orders.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class AddMultiStationKitchen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CategoryId",
            table: "order_lines",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "CategoryName",
            table: "order_lines",
            type: "character varying(120)",
            maxLength: 120,
            nullable: false,
            defaultValue: "General"
        );

        migrationBuilder.AddColumn<string>(
            name: "PreparationStationCode",
            table: "order_lines",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "GENERAL"
        );

        migrationBuilder.AddColumn<string>(
            name: "PreparationStationName",
            table: "order_lines",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            defaultValue: "General"
        );

        migrationBuilder.CreateTable(
            name: "order_kitchen_stations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                Name = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                Status = table.Column<int>(type: "integer", nullable: false),
                PreparationStartedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                ReadyAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_kitchen_stations", x => x.Id);
                table.ForeignKey(
                    name: "FK_order_kitchen_stations_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_order_kitchen_stations_OrderId_Code",
            table: "order_kitchen_stations",
            columns: new[] { "OrderId", "Code" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "order_kitchen_stations");

        migrationBuilder.DropColumn(name: "CategoryId", table: "order_lines");

        migrationBuilder.DropColumn(name: "CategoryName", table: "order_lines");

        migrationBuilder.DropColumn(name: "PreparationStationCode", table: "order_lines");

        migrationBuilder.DropColumn(name: "PreparationStationName", table: "order_lines");
    }
}
