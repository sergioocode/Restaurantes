using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read.Migrations;

/// <inheritdoc />
public partial class AddInboxMessages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(
                    type: "character varying(300)",
                    maxLength: 300,
                    nullable: false
                ),
                ProcessedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_ProcessedAtUtc",
            table: "inbox_messages",
            column: "ProcessedAtUtc"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "inbox_messages");
    }
}
