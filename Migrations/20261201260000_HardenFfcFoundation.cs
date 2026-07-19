using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201260000_HardenFfcFoundation")]
public partial class HardenFfcFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SECTION: Repair the only legacy state that can be corrected without ambiguity.
        migrationBuilder.Sql(
            """
            UPDATE "FfcProjects"
            SET "IsDelivered" = TRUE,
                "DeliveredOn" = COALESCE("DeliveredOn", "InstalledOn"),
                "UpdatedAt" = now()
            WHERE "IsInstalled" = TRUE
              AND "IsDelivered" = FALSE;
            """);

        // SECTION: Stop deployment with a precise message rather than silently discarding data.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "FfcRecords"
                    WHERE "IsDeleted" = FALSE
                    GROUP BY "CountryId", "Year"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'FFC foundation migration blocked: duplicate active country/year records exist.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM "FfcProjects"
                    WHERE "LinkedProjectId" IS NOT NULL
                    GROUP BY "FfcRecordId", "LinkedProjectId"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'FFC foundation migration blocked: the same PRISM project is linked more than once to an FFC record.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM "FfcProjects"
                    WHERE "DeliveredOn" IS NOT NULL
                      AND "InstalledOn" IS NOT NULL
                      AND "InstalledOn" < "DeliveredOn"
                ) THEN
                    RAISE EXCEPTION 'FFC foundation migration blocked: one or more installation dates precede delivery dates.';
                END IF;
            END $$;
            """);

        migrationBuilder.DropIndex(
            name: "IX_FfcRecords_CountryId_Year",
            table: "FfcRecords");

        migrationBuilder.CreateIndex(
            name: "UX_FfcRecords_CountryId_Year_Active",
            table: "FfcRecords",
            columns: new[] { "CountryId", "Year" },
            unique: true,
            filter: "\"IsDeleted\" = FALSE");

        migrationBuilder.CreateIndex(
            name: "UX_FfcProjects_Record_LinkedProject",
            table: "FfcProjects",
            columns: new[] { "FfcRecordId", "LinkedProjectId" },
            unique: true,
            filter: "\"LinkedProjectId\" IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_FfcProjects_Installed_RequiresDelivered",
            table: "FfcProjects",
            sql: "\"IsInstalled\" = FALSE OR \"IsDelivered\" = TRUE");

        migrationBuilder.AddCheckConstraint(
            name: "CK_FfcProjects_InstallationDate_NotBeforeDeliveryDate",
            table: "FfcProjects",
            sql: "\"DeliveredOn\" IS NULL OR \"InstalledOn\" IS NULL OR \"InstalledOn\" >= \"DeliveredOn\"");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_FfcProjects_Installed_RequiresDelivered",
            table: "FfcProjects");

        migrationBuilder.DropCheckConstraint(
            name: "CK_FfcProjects_InstallationDate_NotBeforeDeliveryDate",
            table: "FfcProjects");

        migrationBuilder.DropIndex(
            name: "UX_FfcProjects_Record_LinkedProject",
            table: "FfcProjects");

        migrationBuilder.DropIndex(
            name: "UX_FfcRecords_CountryId_Year_Active",
            table: "FfcRecords");

        migrationBuilder.CreateIndex(
            name: "IX_FfcRecords_CountryId_Year",
            table: "FfcRecords",
            columns: new[] { "CountryId", "Year" });
    }
}
