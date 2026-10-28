using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Migrations;

public partial class AddReportingOrderZoneName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE \"order_reporting_facts\" ADD COLUMN IF NOT EXISTS \"ZoneName\" character varying(80) NOT NULL DEFAULT '';"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE \"order_reporting_facts\" DROP COLUMN IF EXISTS \"ZoneName\";"
        );
    }
}
