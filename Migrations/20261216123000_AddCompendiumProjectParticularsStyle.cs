using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216123000_AddCompendiumProjectParticularsStyle")]
public partial class AddCompendiumProjectParticularsStyle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProjectParticularsStyle",
            table: "CompendiumPresets",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "Panel");

        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 11
            WHERE "SettingsSchemaVersion" <= 10;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 11,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 10);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 10
            WHERE "SettingsSchemaVersion" >= 11;
            """);

        migrationBuilder.DropColumn(
            name: "ProjectParticularsStyle",
            table: "CompendiumPresets");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 10,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 11);
    }
}
