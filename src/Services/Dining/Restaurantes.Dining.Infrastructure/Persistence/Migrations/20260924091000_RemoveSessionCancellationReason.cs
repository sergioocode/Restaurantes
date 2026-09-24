using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Dining.Infrastructure.Persistence;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DiningDbContext))]
[Migration("20260924091000_RemoveSessionCancellationReason")]
public partial class RemoveSessionCancellationReason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CancellationReason", table: "dining_sessions");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CancellationReason",
            table: "dining_sessions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: ""
        );
    }
}
