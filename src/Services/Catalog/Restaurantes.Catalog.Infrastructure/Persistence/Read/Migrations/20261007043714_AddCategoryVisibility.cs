using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Read.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowInCommander",
                table: "category_views",
                type: "boolean",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "ShowInCustomerQr",
                table: "category_views",
                type: "boolean",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "ShowInPos",
                table: "category_views",
                type: "boolean",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE category_views ALTER COLUMN "ShowInPos" DROP DEFAULT;
                ALTER TABLE category_views ALTER COLUMN "ShowInCommander" DROP DEFAULT;
                ALTER TABLE category_views ALTER COLUMN "ShowInCustomerQr" DROP DEFAULT;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ShowInCommander", table: "category_views");

            migrationBuilder.DropColumn(name: "ShowInCustomerQr", table: "category_views");

            migrationBuilder.DropColumn(name: "ShowInPos", table: "category_views");
        }
    }
}
