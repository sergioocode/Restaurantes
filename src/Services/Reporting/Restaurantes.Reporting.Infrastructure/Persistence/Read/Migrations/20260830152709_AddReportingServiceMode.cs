using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddReportingServiceMode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ServiceMode",
            table: "order_reporting_facts",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "DineIn"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ServiceMode", table: "order_reporting_facts");
    }
}
