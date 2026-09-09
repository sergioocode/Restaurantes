using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Identity.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialIdentityWrite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "staff_users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Username = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                NormalizedUsername = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false
                ),
                DisplayName = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: false
                ),
                PasswordHash = table.Column<string>(
                    type: "character varying(600)",
                    maxLength: 600,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_staff_users", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "user_restaurant_accesses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
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

        migrationBuilder.CreateIndex(
            name: "IX_staff_users_NormalizedUsername",
            table: "staff_users",
            column: "NormalizedUsername",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_user_restaurant_accesses_UserId_RestaurantId",
            table: "user_restaurant_accesses",
            columns: new[] { "UserId", "RestaurantId" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_restaurant_accesses");

        migrationBuilder.DropTable(name: "staff_users");
    }
}
