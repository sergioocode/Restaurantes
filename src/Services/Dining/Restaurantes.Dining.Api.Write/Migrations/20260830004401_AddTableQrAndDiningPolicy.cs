using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Api.Write.Migrations;

/// <inheritdoc />
public partial class AddTableQrAndDiningPolicy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "QrCode",
            table: "restaurant_tables",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE restaurant_tables
            SET "QrCode" = upper(replace(gen_random_uuid()::text, '-', ''))
            WHERE "QrCode" IS NULL;
            """
        );

        migrationBuilder.AlterColumn<string>(
            name: "QrCode",
            table: "restaurant_tables",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64,
            oldNullable: true
        );

        migrationBuilder.CreateTable(
            name: "dining_restaurant_policies",
            columns: table => new
            {
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                ReleaseQrTableOnFullPayment = table.Column<bool>(type: "boolean", nullable: false),
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

        migrationBuilder.CreateIndex(
            name: "IX_restaurant_tables_QrCode",
            table: "restaurant_tables",
            column: "QrCode",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "dining_restaurant_policies");

        migrationBuilder.DropIndex(name: "IX_restaurant_tables_QrCode", table: "restaurant_tables");

        migrationBuilder.DropColumn(name: "QrCode", table: "restaurant_tables");
    }
}
