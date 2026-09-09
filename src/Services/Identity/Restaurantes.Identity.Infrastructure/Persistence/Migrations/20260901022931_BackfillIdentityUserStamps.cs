using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Identity.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class BackfillIdentityUserStamps : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE staff_users
            SET "SecurityStamp" = "Id"::text
            WHERE "SecurityStamp" IS NULL OR "SecurityStamp" = '';

            UPDATE staff_users
            SET "ConcurrencyStamp" = "Id"::text
            WHERE "ConcurrencyStamp" IS NULL OR "ConcurrencyStamp" = '';
            """
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
