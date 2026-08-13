using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261208120000_AddBrochureInstitutionalSectionLabels")]
public partial class AddBrochureInstitutionalSectionLabels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PrintProcurementHeading",
            table: "BrochurePresets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PrintContactsHeading",
            table: "BrochurePresets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PrintDevelopingAgencyHeading",
            table: "BrochurePresets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PrintManufacturingAgencyHeading",
            table: "BrochurePresets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PrintVisionaryHeading",
            table: "BrochurePresets",
            type: "character varying(160)",
            maxLength: 160,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PrintNewSimulatorsHeading",
            table: "BrochurePresets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "BrochurePresets",
            type: "integer",
            nullable: false,
            defaultValue: 4,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 3);

        // Preserve the exact Phase 21.1 output for existing shared brochures while
        // converting every PRISM-authored compact-page label into durable editable data.
        migrationBuilder.Sql("""
            UPDATE "BrochurePresets"
            SET "PrintProcurementHeading" = COALESCE("PrintProcurementHeading", 'Procurement:'),
                "PrintContactsHeading" = COALESCE("PrintContactsHeading", 'CONTACTS'),
                "PrintDevelopingAgencyHeading" = COALESCE("PrintDevelopingAgencyHeading", 'Developing Agency'),
                "PrintManufacturingAgencyHeading" = COALESCE("PrintManufacturingAgencyHeading", 'Manufacturing Agency'),
                "PrintVisionaryHeading" = COALESCE("PrintVisionaryHeading", 'Visionary Horizons & Strategic Objectives'),
                "PrintNewSimulatorsHeading" = COALESCE("PrintNewSimulatorsHeading", 'New Simulators.'),
                "SettingsSchemaVersion" = 4;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PrintProcurementHeading", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "PrintContactsHeading", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "PrintDevelopingAgencyHeading", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "PrintManufacturingAgencyHeading", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "PrintVisionaryHeading", table: "BrochurePresets");
        migrationBuilder.DropColumn(name: "PrintNewSimulatorsHeading", table: "BrochurePresets");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "BrochurePresets",
            type: "integer",
            nullable: false,
            defaultValue: 3,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 4);
    }
}
