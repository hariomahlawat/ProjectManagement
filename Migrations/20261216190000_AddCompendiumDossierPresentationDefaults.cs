using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216190000_AddCompendiumDossierPresentationDefaults")]
public partial class AddCompendiumDossierPresentationDefaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DefaultBalancedTextFlowMode",
            table: "CompendiumPresets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "FlowBelowImage");

        migrationBuilder.AddColumn<string>(
            name: "DefaultDossierLayout",
            table: "CompendiumPresets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Automatic");

        migrationBuilder.AddColumn<string>(
            name: "DefaultImageFitMode",
            table: "CompendiumPresets",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Fill");

        migrationBuilder.AddColumn<string>(
            name: "BalancedTextFlowModeOverride",
            table: "CompendiumPresetProjects",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DossierLayoutOverride",
            table: "CompendiumPresetProjects",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ImageFitModeOverride",
            table: "CompendiumPresetProjects",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        // Preserve the effective appearance of every existing v12 dossier while converting
        // legacy effective values to v13 nullable override intent.
        migrationBuilder.Sql("""
            UPDATE "CompendiumPresetProjects"
            SET "DossierLayoutOverride" = CASE
                    WHEN COALESCE("DossierLayout", 'Automatic') <> 'Automatic' THEN "DossierLayout"
                    ELSE NULL
                END,
                "BalancedTextFlowModeOverride" = CASE
                    WHEN COALESCE("BalancedTextFlowMode", 'FlowBelowImage') <> 'FlowBelowImage' THEN "BalancedTextFlowMode"
                    ELSE NULL
                END,
                "ImageFitModeOverride" = CASE
                    WHEN COALESCE("ImageFitMode", 'Fill') <> 'Fill' THEN "ImageFitMode"
                    ELSE NULL
                END;
            """);

        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 13
            WHERE "SettingsSchemaVersion" <= 12;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Legacy effective snapshot columns were deliberately retained, so rollback does not
        // lose the presentation that a project resolved to under schema 13.
        migrationBuilder.Sql("""
            UPDATE "CompendiumPresets"
            SET "SettingsSchemaVersion" = 12
            WHERE "SettingsSchemaVersion" = 13;
            """);

        migrationBuilder.DropColumn(name: "BalancedTextFlowModeOverride", table: "CompendiumPresetProjects");
        migrationBuilder.DropColumn(name: "DossierLayoutOverride", table: "CompendiumPresetProjects");
        migrationBuilder.DropColumn(name: "ImageFitModeOverride", table: "CompendiumPresetProjects");
        migrationBuilder.DropColumn(name: "DefaultBalancedTextFlowMode", table: "CompendiumPresets");
        migrationBuilder.DropColumn(name: "DefaultDossierLayout", table: "CompendiumPresets");
        migrationBuilder.DropColumn(name: "DefaultImageFitMode", table: "CompendiumPresets");
    }
}
