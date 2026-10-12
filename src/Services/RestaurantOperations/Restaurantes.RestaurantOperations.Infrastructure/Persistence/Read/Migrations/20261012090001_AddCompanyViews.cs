using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read.Migrations;

public partial class AddCompanyViews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "company_views",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LegalName = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                TaxId = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false
                ),
                FiscalAddress = table.Column<string>(
                    type: "character varying(300)",
                    maxLength: 300,
                    nullable: false
                ),
                Phone = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: true
                ),
                Email = table.Column<string>(
                    type: "character varying(254)",
                    maxLength: 254,
                    nullable: true
                ),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table => table.PrimaryKey("PK_company_views", x => x.Id)
        );

        migrationBuilder.AddColumn<Guid>(
            name: "CompanyId",
            table: "restaurant_views",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_company_views_TaxId",
            table: "company_views",
            column: "TaxId",
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "company_views");
        migrationBuilder.DropColumn(name: "CompanyId", table: "restaurant_views");
    }
}
