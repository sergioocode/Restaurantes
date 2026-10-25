using Microsoft.EntityFrameworkCore.Migrations;

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

public partial class AddVirtualTakeawaySessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "TableId",
            table: "dining_sessions",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid"
        );
        migrationBuilder.Sql(
            "ALTER TABLE dining_sessions ADD COLUMN \"CustomerName\" character varying(80) NOT NULL DEFAULT ''; ALTER TABLE dining_sessions ALTER COLUMN \"CustomerName\" DROP DEFAULT;"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "TableId",
            table: "dining_sessions",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );
        migrationBuilder.DropColumn(name: "CustomerName", table: "dining_sessions");
    }
}
