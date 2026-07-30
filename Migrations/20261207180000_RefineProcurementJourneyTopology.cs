using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207180000_RefineProcurementJourneyTopology")]
public partial class RefineProcurementJourneyTopology : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Price Negotiation is conditional. Commercial Opening remains the
        // mandatory prerequisite for EAS, while PNC is an optional detour.
        migrationBuilder.Sql(
            """
            DELETE FROM "StageDependencyTemplates"
            WHERE "Version" IN ('SDD-1.0', 'SDD-2.0')
              AND UPPER("FromStageCode") = 'EAS'
              AND UPPER("DependsOnStageCode") = 'PNC';

            INSERT INTO "StageDependencyTemplates" ("Version", "FromStageCode", "DependsOnStageCode")
            SELECT versions."Version", 'EAS', 'COB'
            FROM (VALUES ('SDD-1.0'), ('SDD-2.0')) AS versions("Version")
            WHERE EXISTS (
                SELECT 1
                FROM "StageTemplates"
                WHERE "Version" = versions."Version"
                  AND UPPER("Code") = 'EAS'
            )
              AND NOT EXISTS (
                SELECT 1
                FROM "StageDependencyTemplates"
                WHERE "Version" = versions."Version"
                  AND UPPER("FromStageCode") = 'EAS'
                  AND UPPER("DependsOnStageCode") = 'COB'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO "StageDependencyTemplates" ("Version", "FromStageCode", "DependsOnStageCode")
            SELECT versions."Version", 'EAS', 'PNC'
            FROM (VALUES ('SDD-1.0'), ('SDD-2.0')) AS versions("Version")
            WHERE EXISTS (
                SELECT 1
                FROM "StageTemplates"
                WHERE "Version" = versions."Version"
                  AND UPPER("Code") = 'EAS'
            )
              AND NOT EXISTS (
                SELECT 1
                FROM "StageDependencyTemplates"
                WHERE "Version" = versions."Version"
                  AND UPPER("FromStageCode") = 'EAS'
                  AND UPPER("DependsOnStageCode") = 'PNC'
            );
            """);
    }
}
