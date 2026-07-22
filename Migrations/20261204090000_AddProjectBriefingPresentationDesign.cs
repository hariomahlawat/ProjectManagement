using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261204090000_AddProjectBriefingPresentationDesign")]
public partial class AddProjectBriefingPresentationDesign : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PresentationTheme",
            table: "ProjectBriefingDecks",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "EditorialLight");

        migrationBuilder.AddColumn<string>(
            name: "BrandingScope",
            table: "ProjectBriefingDecks",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "AllSlides");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PresentationTheme",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropColumn(
            name: "BrandingScope",
            table: "ProjectBriefingDecks");
    }
}
