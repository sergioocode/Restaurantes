using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResetAuthorizedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM authorized_accounts;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
