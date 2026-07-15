using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201240000_AddErpUsageDailySummaries")]
public partial class AddErpUsageDailySummaries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserActivityDailySummaries",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                ActivityDateIst = table.Column<DateOnly>(type: "date", nullable: false),
                HadNavigation = table.Column<bool>(type: "boolean", nullable: false),
                HadInteractiveHeartbeat = table.Column<bool>(type: "boolean", nullable: false),
                HadAdministrativeAction = table.Column<bool>(type: "boolean", nullable: false),
                HadOperationalAction = table.Column<bool>(type: "boolean", nullable: false),
                FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                NavigationCount = table.Column<int>(type: "integer", nullable: false),
                HeartbeatCount = table.Column<int>(type: "integer", nullable: false),
                AdministrativeActionCount = table.Column<int>(type: "integer", nullable: false),
                OperationalActionCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserActivityDailySummaries", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserActivityDailySummaries_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_UserId_TimeUtc",
            table: "AuditLogs",
            columns: new[] { "UserId", "TimeUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityDailySummaries_ActivityDateIst",
            table: "UserActivityDailySummaries",
            column: "ActivityDateIst");

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityDailySummaries_UserId_ActivityDateIst",
            table: "UserActivityDailySummaries",
            columns: new[] { "UserId", "ActivityDateIst" },
            unique: true);

        // Preserve all activity already captured in the detailed bucket table. The daily
        // table is intentionally not subject to the detailed-bucket retention policy.
        migrationBuilder.Sql("""
            INSERT INTO "UserActivityDailySummaries"
                ("UserId", "ActivityDateIst", "HadNavigation", "HadInteractiveHeartbeat",
                 "HadAdministrativeAction", "HadOperationalAction",
                 "FirstSeenUtc", "LastSeenUtc", "NavigationCount", "HeartbeatCount",
                 "AdministrativeActionCount", "OperationalActionCount")
            SELECT
                "UserId",
                "ActivityDateIst",
                BOOL_OR("HadNavigation"),
                BOOL_OR("HadInteractiveHeartbeat"),
                FALSE,
                FALSE,
                MIN("FirstSeenUtc"),
                MAX("LastSeenUtc"),
                LEAST(2147483647, SUM("NavigationCount")::bigint)::integer,
                LEAST(2147483647, SUM("HeartbeatCount")::bigint)::integer,
                0,
                0
            FROM "UserActivityBuckets"
            GROUP BY "UserId", "ActivityDateIst"
            ON CONFLICT ("UserId", "ActivityDateIst") DO NOTHING;
            """);

        migrationBuilder.Sql("""
            WITH classified AS (
                SELECT
                    a."UserId",
                    (a."TimeUtc" AT TIME ZONE 'UTC' AT TIME ZONE 'Asia/Kolkata')::date AS "ActivityDateIst",
                    a."TimeUtc" AT TIME ZONE 'UTC' AS "TimeUtc",
                    CASE
                        WHEN a."Action" ILIKE ANY (ARRAY[
                            'Project%', 'Projects.%', 'Stage%', 'PlanVersion%', 'Approval%',
                            'Remark%', 'Comments.%', 'ActionTask%', 'ActionSprint%', 'Task%',
                            'Todo%', 'Calendar.%', 'CalendarEvent%', 'Celebration%', 'Document%',
                            'Documents.%', 'DocRepo%', 'Activity%', 'Visit%', 'Training%',
                            'Proliferation%', 'ProjectOfficeReports.%', 'Ipr%', 'Ffc%',
                            'IndustryPartner%', 'Notebook%', 'Notification%'])
                            OR a."Action" ILIKE '%Approved%'
                            OR a."Action" ILIKE '%Rejected%'
                            OR a."Action" ILIKE '%Submitted%'
                            OR a."Action" ILIKE '%Assigned%'
                            OR a."Action" ILIKE '%Completed%'
                            OR a."Action" ILIKE '%Uploaded%'
                            OR a."Action" ILIKE '%Published%'
                        THEN 1
                        ELSE 2
                    END AS "Kind"
                FROM "AuditLogs" a
                INNER JOIN "AspNetUsers" u ON u."Id" = a."UserId"
                WHERE a."UserId" IS NOT NULL
                  AND a."Action" NOT ILIKE ANY (ARRAY[
                      'Login%', 'Logout%', 'Auth%', 'Password%', 'UserActivity%',
                      'ErpUsage%', 'SystemHealth%', 'Session%', 'Antiforgery%'])
            ), aggregated AS (
                SELECT
                    "UserId",
                    "ActivityDateIst",
                    MIN("TimeUtc") AS "FirstSeenUtc",
                    MAX("TimeUtc") AS "LastSeenUtc",
                    COUNT(*) FILTER (WHERE "Kind" = 2)::integer AS "AdministrativeActionCount",
                    COUNT(*) FILTER (WHERE "Kind" = 1)::integer AS "OperationalActionCount"
                FROM classified
                GROUP BY "UserId", "ActivityDateIst"
            )
            INSERT INTO "UserActivityDailySummaries"
                ("UserId", "ActivityDateIst", "HadNavigation", "HadInteractiveHeartbeat",
                 "HadAdministrativeAction", "HadOperationalAction",
                 "FirstSeenUtc", "LastSeenUtc", "NavigationCount", "HeartbeatCount",
                 "AdministrativeActionCount", "OperationalActionCount")
            SELECT
                "UserId",
                "ActivityDateIst",
                FALSE,
                FALSE,
                "AdministrativeActionCount" > 0,
                "OperationalActionCount" > 0,
                "FirstSeenUtc",
                "LastSeenUtc",
                0,
                0,
                "AdministrativeActionCount",
                "OperationalActionCount"
            FROM aggregated
            ON CONFLICT ("UserId", "ActivityDateIst")
            DO UPDATE SET
                "HadAdministrativeAction" = "UserActivityDailySummaries"."HadAdministrativeAction" OR EXCLUDED."HadAdministrativeAction",
                "HadOperationalAction" = "UserActivityDailySummaries"."HadOperationalAction" OR EXCLUDED."HadOperationalAction",
                "FirstSeenUtc" = LEAST("UserActivityDailySummaries"."FirstSeenUtc", EXCLUDED."FirstSeenUtc"),
                "LastSeenUtc" = GREATEST("UserActivityDailySummaries"."LastSeenUtc", EXCLUDED."LastSeenUtc"),
                "AdministrativeActionCount" = LEAST(2147483647, "UserActivityDailySummaries"."AdministrativeActionCount"::bigint + EXCLUDED."AdministrativeActionCount")::integer,
                "OperationalActionCount" = LEAST(2147483647, "UserActivityDailySummaries"."OperationalActionCount"::bigint + EXCLUDED."OperationalActionCount")::integer;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserActivityDailySummaries");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogs_UserId_TimeUtc",
            table: "AuditLogs");
    }
}
