using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261215170000_AddCompendiumNarrativeAlignment")]
    public partial class AddCompendiumNarrativeAlignment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve legacy publications exactly: existing presets remain left-aligned and
            // individual projects inherit the publication default unless explicitly overridden.
            migrationBuilder.AddColumn<string>(
                name: "DefaultNarrativeAlignment",
                table: "CompendiumPresets",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Left");

            migrationBuilder.AddColumn<string>(
                name: "NarrativeAlignmentOverride",
                table: "CompendiumPresetProjects",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "CompendiumPresets"
                SET "SettingsSchemaVersion" = 9
                WHERE "SettingsSchemaVersion" <= 8;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 9,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 8);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "CompendiumPresets"
                SET "SettingsSchemaVersion" = 8
                WHERE "SettingsSchemaVersion" >= 9;
                """);

            migrationBuilder.DropColumn(
                name: "DefaultNarrativeAlignment",
                table: "CompendiumPresets");

            migrationBuilder.DropColumn(
                name: "NarrativeAlignmentOverride",
                table: "CompendiumPresetProjects");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 8,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 9);
        }
    }
}
