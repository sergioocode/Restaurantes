using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurantes.Identity.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ReplacePasswordIdentityWithExternalAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "identity_role_claims");

        migrationBuilder.DropTable(name: "identity_user_claims");

        migrationBuilder.DropTable(name: "identity_user_logins");

        migrationBuilder.DropTable(name: "identity_user_roles");

        migrationBuilder.DropTable(name: "identity_user_tokens");

        migrationBuilder.DropTable(name: "user_restaurant_accesses");

        migrationBuilder.DropTable(name: "identity_roles");

        migrationBuilder.DropPrimaryKey(name: "PK_staff_users", table: "staff_users");

        migrationBuilder.DropIndex(name: "EmailIndex", table: "staff_users");

        migrationBuilder.DropIndex(name: "UserNameIndex", table: "staff_users");

        migrationBuilder.DropColumn(name: "AccessFailedCount", table: "staff_users");

        migrationBuilder.DropColumn(name: "ConcurrencyStamp", table: "staff_users");

        migrationBuilder.DropColumn(name: "EmailConfirmed", table: "staff_users");

        migrationBuilder.DropColumn(name: "LockoutEnabled", table: "staff_users");

        migrationBuilder.DropColumn(name: "LockoutEnd", table: "staff_users");

        migrationBuilder.DropColumn(name: "NormalizedUsername", table: "staff_users");

        migrationBuilder.DropColumn(name: "PasswordHash", table: "staff_users");

        migrationBuilder.DropColumn(name: "PhoneNumber", table: "staff_users");

        migrationBuilder.DropColumn(name: "PhoneNumberConfirmed", table: "staff_users");

        migrationBuilder.DropColumn(name: "SecurityStamp", table: "staff_users");

        migrationBuilder.DropColumn(name: "TwoFactorEnabled", table: "staff_users");

        migrationBuilder.DropColumn(name: "Username", table: "staff_users");

        migrationBuilder.RenameTable(name: "staff_users", newName: "authorized_accounts");

        migrationBuilder.RenameColumn(
            name: "NormalizedEmail",
            table: "authorized_accounts",
            newName: "ProviderSubject"
        );

        migrationBuilder.AlterColumn<string>(
            name: "Email",
            table: "authorized_accounts",
            type: "character varying(320)",
            maxLength: 320,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256,
            oldNullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "Provider",
            table: "authorized_accounts",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<Guid>(
            name: "RestaurantId",
            table: "authorized_accounts",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "Role",
            table: "authorized_accounts",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<string>(
            name: "TenantId",
            table: "authorized_accounts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
        );

        migrationBuilder.AddPrimaryKey(
            name: "PK_authorized_accounts",
            table: "authorized_accounts",
            column: "Id"
        );

        migrationBuilder.Sql("DELETE FROM authorized_accounts;");

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

        migrationBuilder.CreateTable(
            name: "login_tickets",
            columns: table => new
            {
                CodeHash = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false
                ),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                ConsumedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_login_tickets", x => x.CodeHash);
                table.ForeignKey(
                    name: "FK_login_tickets_authorized_accounts_UserId",
                    column: x => x.UserId,
                    principalTable: "authorized_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_authorized_accounts_Provider_Email",
            table: "authorized_accounts",
            columns: new[] { "Provider", "Email" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_authorized_accounts_Provider_TenantId_ProviderSubject",
            table: "authorized_accounts",
            columns: new[] { "Provider", "TenantId", "ProviderSubject" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_login_tickets_UserId",
            table: "login_tickets",
            column: "UserId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "authentication_settings");

        migrationBuilder.DropTable(name: "login_tickets");

        migrationBuilder.DropPrimaryKey(
            name: "PK_authorized_accounts",
            table: "authorized_accounts"
        );

        migrationBuilder.DropIndex(
            name: "IX_authorized_accounts_Provider_Email",
            table: "authorized_accounts"
        );

        migrationBuilder.DropIndex(
            name: "IX_authorized_accounts_Provider_TenantId_ProviderSubject",
            table: "authorized_accounts"
        );

        migrationBuilder.DropColumn(name: "Provider", table: "authorized_accounts");

        migrationBuilder.DropColumn(name: "RestaurantId", table: "authorized_accounts");

        migrationBuilder.DropColumn(name: "Role", table: "authorized_accounts");

        migrationBuilder.DropColumn(name: "TenantId", table: "authorized_accounts");

        migrationBuilder.RenameTable(name: "authorized_accounts", newName: "staff_users");

        migrationBuilder.RenameColumn(
            name: "ProviderSubject",
            table: "staff_users",
            newName: "NormalizedEmail"
        );

        migrationBuilder.AlterColumn<string>(
            name: "Email",
            table: "staff_users",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(320)",
            oldMaxLength: 320
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
            defaultValue: false
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LockoutEnd",
            table: "staff_users",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "NormalizedUsername",
            table: "staff_users",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<string>(
            name: "PasswordHash",
            table: "staff_users",
            type: "character varying(600)",
            maxLength: 600,
            nullable: false,
            defaultValue: ""
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

        migrationBuilder.AddColumn<string>(
            name: "Username",
            table: "staff_users",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddPrimaryKey(name: "PK_staff_users", table: "staff_users", column: "Id");

        migrationBuilder.CreateTable(
            name: "identity_roles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
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
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
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
            name: "user_restaurant_accesses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                ValidFromUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                ValidUntilUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_restaurant_accesses", x => x.Id);
                table.ForeignKey(
                    name: "FK_user_restaurant_accesses_staff_users_UserId",
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
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true),
                RoleId = table.Column<Guid>(type: "uuid", nullable: false),
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
            name: "UserNameIndex",
            table: "staff_users",
            column: "NormalizedUsername",
            unique: true
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

        migrationBuilder.CreateIndex(
            name: "IX_user_restaurant_accesses_UserId_RestaurantId",
            table: "user_restaurant_accesses",
            columns: new[] { "UserId", "RestaurantId" },
            unique: true
        );
    }
}
