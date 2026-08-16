using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261208200000_AddCompendiumBalancedTextFlow")]
    public partial class AddCompendiumBalancedTextFlow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows deliberately retain the legacy rigid two-column treatment.
            migrationBuilder.AddColumn<string>(
                name: "BalancedTextFlowMode",
                table: "CompendiumPresetProjects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "SideColumn");

            migrationBuilder.Sql("""
                UPDATE "CompendiumPresets"
                SET "SettingsSchemaVersion" = 8
                WHERE "SettingsSchemaVersion" <= 7;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 8,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 7);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BalancedTextFlowMode",
                table: "CompendiumPresetProjects");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 7,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 8);
        }
    }
}
