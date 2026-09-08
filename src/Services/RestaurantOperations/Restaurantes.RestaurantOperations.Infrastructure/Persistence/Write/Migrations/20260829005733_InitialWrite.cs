using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write.Migrations;

/// <inheritdoc />
public partial class InitialWrite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(
                    type: "character varying(300)",
                    maxLength: 300,
                    nullable: false
                ),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                ProcessedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                Error = table.Column<string>(
                    type: "character varying(2000)",
                    maxLength: 2000,
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "restaurants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false
                ),
                Name = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false
                ),
                Address = table.Column<string>(
                    type: "character varying(300)",
                    maxLength: 300,
                    nullable: false
                ),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_restaurants", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedAtUtc_OccurredAtUtc",
            table: "outbox_messages",
            columns: new[] { "ProcessedAtUtc", "OccurredAtUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_restaurants_Code",
            table: "restaurants",
            column: "Code",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages");

        migrationBuilder.DropTable(name: "restaurants");
    }
}
