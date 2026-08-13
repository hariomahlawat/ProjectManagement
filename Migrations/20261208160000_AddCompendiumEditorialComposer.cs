using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261208160000_AddCompendiumEditorialComposer")]
    public partial class AddCompendiumEditorialComposer : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NarrativeSource",
                table: "CompendiumPresets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ProjectBrief");

            migrationBuilder.AddColumn<string>(
                name: "GroupingMode",
                table: "CompendiumPresets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "TechnicalCategory");

            migrationBuilder.AddColumn<string>(
                name: "SortMode",
                table: "CompendiumPresets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "CustomSectionName",
                table: "CompendiumPresetProjects",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            // Existing schema-v3 Compendiums were description-led. Preserve that authored
            // publication intent; Project Brief is the default only for newly created v4 work.
            migrationBuilder.Sql(
                "UPDATE \"CompendiumPresets\" SET \"NarrativeSource\" = 'ProjectDescription' WHERE \"SettingsSchemaVersion\" <= 3;");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 4,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 3);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NarrativeSource", table: "CompendiumPresets");
            migrationBuilder.DropColumn(name: "GroupingMode", table: "CompendiumPresets");
            migrationBuilder.DropColumn(name: "SortMode", table: "CompendiumPresets");
            migrationBuilder.DropColumn(name: "CustomSectionName", table: "CompendiumPresetProjects");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 4);
        }
    }
}
