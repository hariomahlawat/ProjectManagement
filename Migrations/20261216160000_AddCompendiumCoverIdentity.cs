using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216160000_AddCompendiumCoverIdentity")]
public partial class AddCompendiumCoverIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PublicationTheme",
            table: "CompendiumPresets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "InstitutionalGreen");

        migrationBuilder.AddColumn<string>(
            name: "CoverBackgroundTreatment",
            table: "CompendiumPresets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Solid");

        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 12
            WHERE "SettingsSchemaVersion" <= 11;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 12,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 11);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 11
            WHERE "SettingsSchemaVersion" >= 12;
            """);

        migrationBuilder.DropColumn(
            name: "PublicationTheme",
            table: "CompendiumPresets");

        migrationBuilder.DropColumn(
            name: "CoverBackgroundTreatment",
            table: "CompendiumPresets");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 11,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 12);
    }
}
