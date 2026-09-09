using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDiningLocationType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LocationType",
            table: "restaurant_tables",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Table"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LocationType", table: "restaurant_tables");
    }
}
