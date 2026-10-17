using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261217000002_VersionPlanningInputAuthority")]
public sealed class VersionPlanningInputAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Zero deliberately classifies every existing row as legacy. Shared globals
        // may contain abandoned private work; existing proposal policies were incomplete.
        migrationBuilder.AddColumn<int>(
            name: "PublishedPlanningInputVersion", table: "Projects", type: "integer",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "PlanningInputVersion", table: "PlanVersions", type: "integer",
            nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PlanningInputVersion", table: "PlanVersions");
        migrationBuilder.DropColumn(name: "PublishedPlanningInputVersion", table: "Projects");
    }
}
