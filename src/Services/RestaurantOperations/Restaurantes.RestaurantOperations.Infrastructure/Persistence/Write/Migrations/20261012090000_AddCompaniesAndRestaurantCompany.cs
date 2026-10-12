using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write.Migrations;

public partial class AddCompaniesAndRestaurantCompany : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "companies",
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
            constraints: table => table.PrimaryKey("PK_companies", x => x.Id)
        );

        migrationBuilder.AddColumn<Guid>(
            name: "CompanyId",
            table: "restaurants",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_companies_TaxId",
            table: "companies",
            column: "TaxId",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_restaurants_CompanyId",
            table: "restaurants",
            column: "CompanyId"
        );

        migrationBuilder.AddForeignKey(
            name: "FK_restaurants_companies_CompanyId",
            table: "restaurants",
            column: "CompanyId",
            principalTable: "companies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_restaurants_companies_CompanyId",
            table: "restaurants"
        );
        migrationBuilder.DropTable(name: "companies");
        migrationBuilder.DropIndex(name: "IX_restaurants_CompanyId", table: "restaurants");
        migrationBuilder.DropColumn(name: "CompanyId", table: "restaurants");
    }
}
