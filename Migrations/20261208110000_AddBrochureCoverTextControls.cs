using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261208110000_AddBrochureCoverTextControls")]
public partial class AddBrochureCoverTextControls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FrontCoverKicker",
            table: "BrochurePresets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FrontCoverDescriptor",
            table: "BrochurePresets",
            type: "character varying(160)",
            maxLength: 160,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowFrontCoverTitle",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowFrontCoverSubtitle",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowFrontCoverEdition",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowFrontCoverStrapline",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "BackCoverKicker",
            table: "BrochurePresets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BackCoverStrapline",
            table: "BrochurePresets",
            type: "character varying(180)",
            maxLength: 180,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BackCoverEdition",
            table: "BrochurePresets",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "BrochurePresets",
            type: "integer",
            nullable: false,
            defaultValue: 2,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 1);

        // Existing shared brochures preserve their current visible front/back cover copy.
        // New values remain user-editable and nullable so clearing a field suppresses it.
        migrationBuilder.Sql("""
            UPDATE "BrochurePresets"
            SET "FrontCoverKicker" = 'Simulator Development Division',
                "FrontCoverDescriptor" = CASE
                    WHEN "CoverStyle" = 'Contemporary' THEN 'Capability Publication · Contemporary Edition'
                    ELSE 'Official Capability Publication'
                END,
                "BackCoverKicker" = 'Simulator Development Division',
                "BackCoverStrapline" = "Strapline",
                "BackCoverEdition" = "Edition",
                "SettingsSchemaVersion" = 2;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FrontCoverKicker", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "FrontCoverDescriptor", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowFrontCoverTitle", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowFrontCoverSubtitle", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowFrontCoverEdition", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowFrontCoverStrapline", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "BackCoverKicker", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "BackCoverStrapline", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "BackCoverEdition", table: "BrochurePresets");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "BrochurePresets",
            type: "integer",
            nullable: false,
            defaultValue: 1,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 2);
    }
}
