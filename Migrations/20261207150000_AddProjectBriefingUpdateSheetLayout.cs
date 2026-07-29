using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207150000_AddProjectBriefingUpdateSheetLayout")]
public partial class AddProjectBriefingUpdateSheetLayout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IncludeCoverSlide",
            table: "ProjectBriefingDecks",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IncludePortfolioSummarySlide",
            table: "ProjectBriefingDecks",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "Layout",
            table: "ProjectBriefingDecks",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "StandardBriefing");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IncludeCoverSlide",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropColumn(
            name: "IncludePortfolioSummarySlide",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropColumn(
            name: "Layout",
            table: "ProjectBriefingDecks");
    }
}
