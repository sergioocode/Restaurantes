using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Dining.Infrastructure.Persistence;

#nullable disable

namespace Restaurantes.Dining.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DiningDbContext))]
[Migration("20260924090000_AddSessionOpenedByUser")]
public partial class AddSessionOpenedByUser : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "OpenedByUserId",
            table: "dining_sessions",
            type: "uuid",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OpenedByUserId", table: "dining_sessions");
    }
}
