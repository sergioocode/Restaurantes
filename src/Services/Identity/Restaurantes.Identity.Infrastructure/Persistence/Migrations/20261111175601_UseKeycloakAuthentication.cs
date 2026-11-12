using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurantes.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UseKeycloakAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT lower(trim("Email"))
                        FROM authorized_accounts
                        GROUP BY lower(trim("Email"))
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot migrate Identity to Keycloak: duplicate account emails must be resolved first.';
                    END IF;
                END $$;
                """
            );
            migrationBuilder.Sql(
                """
                UPDATE authorized_accounts
                SET "Email" = lower(trim("Email")),
                    "Provider" = 'Keycloak',
                    "ProviderSubject" = NULL,
                    "TenantId" = NULL;
                """
            );
            migrationBuilder.DropTable(name: "authentication_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE authorized_accounts
                SET "Provider" = 'Microsoft',
                    "ProviderSubject" = NULL,
                    "TenantId" = NULL;
                """
            );
            migrationBuilder.CreateTable(
                name: "authentication_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    ActiveProvider = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_settings", x => x.Id);
                }
            );
            migrationBuilder.InsertData(
                table: "authentication_settings",
                columns: ["Id", "ActiveProvider"],
                values: [1, "Microsoft"]
            );
        }
    }
}
