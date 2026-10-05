using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Reporting.Infrastructure.Persistence.Read.Migrations;

[DbContext(typeof(ReportingReadDbContext))]
[Migration("20261005010001_AddCompletedSaleBusinessDate")]
public partial class AddCompletedSaleBusinessDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "BusinessDate",
            table: "completed_sale_facts",
            type: "date",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_completed_sale_facts_BusinessDate_RestaurantId",
            table: "completed_sale_facts",
            columns: new[] { "BusinessDate", "RestaurantId" }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_completed_sale_facts_BusinessDate_RestaurantId",
            table: "completed_sale_facts"
        );
        migrationBuilder.DropColumn(name: "BusinessDate", table: "completed_sale_facts");
    }
}
