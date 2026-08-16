using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216110000_AddCompendiumProjectAdditionalNote")]
public partial class AddCompendiumProjectAdditionalNote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AdditionalNote",
            table: "CompendiumPresetProjects",
            type: "text",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 10
            WHERE "SettingsSchemaVersion" <= 9;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 10,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 9);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 9
            WHERE "SettingsSchemaVersion" >= 10;
            """);

        migrationBuilder.DropColumn(
            name: "AdditionalNote",
            table: "CompendiumPresetProjects");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 9,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 10);
    }
}
