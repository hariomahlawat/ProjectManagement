using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261217000001_AddProposalOwnedPlanningInputs")]
public sealed class AddProposalOwnedPlanningInputs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "SkipHolidays", table: "PlanVersions", type: "boolean",
            nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<int>(
            name: "SortOrder", table: "StagePlans", type: "integer",
            nullable: false, defaultValue: 0);

        // Older revisions did not persist holiday policy independently. This is
        // deterministic compatibility backfill, not exact historical recovery.
        migrationBuilder.Sql("""
            UPDATE "PlanVersions" AS plan
            SET "SkipHolidays" = settings."SkipHolidays"
            FROM "ProjectScheduleSettings" AS settings
            WHERE settings."ProjectId" = plan."ProjectId";

            UPDATE "StagePlans" AS stage
            SET "SortOrder" = COALESCE(
                (SELECT duration."SortOrder"
                 FROM "ProjectPlanDurations" AS duration
                 JOIN "PlanVersions" AS plan ON plan."ProjectId" = duration."ProjectId"
                 WHERE plan."Id" = stage."PlanVersionId"
                   AND UPPER(duration."StageCode") = UPPER(stage."StageCode")
                 ORDER BY duration."Id" LIMIT 1),
                (SELECT official."SortOrder"
                 FROM "ProjectStages" AS official
                 JOIN "PlanVersions" AS plan ON plan."ProjectId" = official."ProjectId"
                 WHERE plan."Id" = stage."PlanVersionId"
                   AND UPPER(official."StageCode") = UPPER(stage."StageCode")
                 ORDER BY official."Id" LIMIT 1),
                (SELECT template."Sequence"
                 FROM "StageTemplates" AS template
                 JOIN "Projects" AS project ON template."Version" = COALESCE(project."WorkflowVersion", 'SDD-1.0')
                 JOIN "PlanVersions" AS plan ON plan."ProjectId" = project."Id"
                 WHERE plan."Id" = stage."PlanVersionId"
                   AND UPPER(template."Code") = UPPER(stage."StageCode")
                 ORDER BY template."Id" LIMIT 1),
                stage."Id");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SortOrder", table: "StagePlans");
        migrationBuilder.DropColumn(name: "SkipHolidays", table: "PlanVersions");
    }
}
