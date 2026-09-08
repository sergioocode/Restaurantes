using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddReportingCustomerName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CustomerName",
            table: "order_reporting_facts",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            defaultValue: ""
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CustomerName", table: "order_reporting_facts");
    }
}
