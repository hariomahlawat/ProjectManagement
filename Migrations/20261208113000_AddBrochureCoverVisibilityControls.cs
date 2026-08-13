using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261208113000_AddBrochureCoverVisibilityControls")]
public partial class AddBrochureCoverVisibilityControls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ShowFrontCoverKicker",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowFrontCoverDescriptor",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowBackCoverKicker",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowBackCoverStrapline",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowBackCoverEdition",
            table: "BrochurePresets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "BrochurePresets",
            type: "integer",
            nullable: false,
            defaultValue: 3,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 2);

        // Phase 21 presets already rendered all five lines whenever their stored text
        // was non-empty. Preserve that visual state while making visibility independently
        // controllable from the builder from schema version 3 onward.
        migrationBuilder.Sql("""
            UPDATE "BrochurePresets"
            SET "ShowFrontCoverKicker" = TRUE,
                "ShowFrontCoverDescriptor" = TRUE,
                "ShowBackCoverKicker" = TRUE,
                "ShowBackCoverStrapline" = TRUE,
                "ShowBackCoverEdition" = TRUE,
                "SettingsSchemaVersion" = 3;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShowFrontCoverKicker", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowFrontCoverDescriptor", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowBackCoverKicker", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowBackCoverStrapline", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "ShowBackCoverEdition", table: "BrochurePresets");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "BrochurePresets",
            type: "integer",
            nullable: false,
            defaultValue: 2,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 3);
    }
}
