using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurantes.Identity.Api.Write.Migrations;

/// <inheritdoc />
public partial class MigrateToAspNetCoreIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameIndex(
            name: "IX_staff_users_NormalizedUsername",
            table: "staff_users",
            newName: "UserNameIndex"
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "ValidFromUtc",
            table: "user_restaurant_accesses",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: DateTime.UnixEpoch
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "ValidUntilUtc",
            table: "user_restaurant_accesses",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "AccessFailedCount",
            table: "staff_users",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<string>(
            name: "ConcurrencyStamp",
            table: "staff_users",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "Email",
            table: "staff_users",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true
        );

        migrationBuilder.AddColumn<bool>(
            name: "EmailConfirmed",
            table: "staff_users",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<bool>(
            name: "LockoutEnabled",
            table: "staff_users",
            type: "boolean",
            nullable: false,
            defaultValue: true
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LockoutEnd",
            table: "staff_users",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "NormalizedEmail",
            table: "staff_users",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "PhoneNumber",
            table: "staff_users",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<bool>(
            name: "PhoneNumberConfirmed",
            table: "staff_users",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<string>(
            name: "SecurityStamp",
            table: "staff_users",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<bool>(
            name: "TwoFactorEnabled",
            table: "staff_users",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.CreateTable(
            name: "identity_roles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                NormalizedName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_identity_roles", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "identity_user_claims",
            columns: table => new
            {
                Id = table
                    .Column<int>(type: "integer", nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                    ),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_identity_user_claims", x => x.Id);
                table.ForeignKey(
                    name: "FK_identity_user_claims_staff_users_UserId",
                    column: x => x.UserId,
                    principalTable: "staff_users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "identity_user_logins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                ProviderKey = table.Column<string>(type: "text", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_identity_user_logins",
                    x => new { x.LoginProvider, x.ProviderKey }
                );
                table.ForeignKey(
                    name: "FK_identity_user_logins_staff_users_UserId",
                    column: x => x.UserId,
                    principalTable: "staff_users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "identity_user_tokens",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_identity_user_tokens",
                    x => new
                    {
                        x.UserId,
                        x.LoginProvider,
                        x.Name,
                    }
                );
                table.ForeignKey(
                    name: "FK_identity_user_tokens_staff_users_UserId",
                    column: x => x.UserId,
                    principalTable: "staff_users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "identity_role_claims",
            columns: table => new
            {
                Id = table
                    .Column<int>(type: "integer", nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                    ),
                RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_identity_role_claims", x => x.Id);
                table.ForeignKey(
                    name: "FK_identity_role_claims_identity_roles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "identity_roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "identity_user_roles",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RoleId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_identity_user_roles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_identity_user_roles_identity_roles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "identity_roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
                table.ForeignKey(
                    name: "FK_identity_user_roles_staff_users_UserId",
                    column: x => x.UserId,
                    principalTable: "staff_users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "staff_users",
            column: "NormalizedEmail"
        );

        migrationBuilder.CreateIndex(
            name: "IX_identity_role_claims_RoleId",
            table: "identity_role_claims",
            column: "RoleId"
        );

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            table: "identity_roles",
            column: "NormalizedName",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_identity_user_claims_UserId",
            table: "identity_user_claims",
            column: "UserId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_identity_user_logins_UserId",
            table: "identity_user_logins",
            column: "UserId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_identity_user_roles_RoleId",
            table: "identity_user_roles",
            column: "RoleId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "identity_role_claims");

        migrationBuilder.DropTable(name: "identity_user_claims");

        migrationBuilder.DropTable(name: "identity_user_logins");

        migrationBuilder.DropTable(name: "identity_user_roles");

        migrationBuilder.DropTable(name: "identity_user_tokens");

        migrationBuilder.DropTable(name: "identity_roles");

        migrationBuilder.DropIndex(name: "EmailIndex", table: "staff_users");

        migrationBuilder.DropColumn(name: "ValidFromUtc", table: "user_restaurant_accesses");

        migrationBuilder.DropColumn(name: "ValidUntilUtc", table: "user_restaurant_accesses");

        migrationBuilder.DropColumn(name: "AccessFailedCount", table: "staff_users");

        migrationBuilder.DropColumn(name: "ConcurrencyStamp", table: "staff_users");

        migrationBuilder.DropColumn(name: "Email", table: "staff_users");

        migrationBuilder.DropColumn(name: "EmailConfirmed", table: "staff_users");

        migrationBuilder.DropColumn(name: "LockoutEnabled", table: "staff_users");

        migrationBuilder.DropColumn(name: "LockoutEnd", table: "staff_users");

        migrationBuilder.DropColumn(name: "NormalizedEmail", table: "staff_users");

        migrationBuilder.DropColumn(name: "PhoneNumber", table: "staff_users");

        migrationBuilder.DropColumn(name: "PhoneNumberConfirmed", table: "staff_users");

        migrationBuilder.DropColumn(name: "SecurityStamp", table: "staff_users");

        migrationBuilder.DropColumn(name: "TwoFactorEnabled", table: "staff_users");

        migrationBuilder.RenameIndex(
            name: "UserNameIndex",
            table: "staff_users",
            newName: "IX_staff_users_NormalizedUsername"
        );
    }
}
