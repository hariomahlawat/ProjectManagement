using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261206100000_AddProjectTotDatePrecision")]
public partial class AddProjectTotDatePrecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "StartDatePrecision", table: "ProjectTots", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "CompletionDatePrecision", table: "ProjectTots", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "ProposedStartDatePrecision", table: "ProjectTotRequests", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "ProposedCompletionDatePrecision", table: "ProjectTotRequests", type: "integer", nullable: false, defaultValue: 0);

        // Partial dates were historically stored as range-boundary DateOnly values.
        // Backfill boundary values conservatively so the migration does not turn an
        // inferred date into a user-asserted exact day.
        migrationBuilder.Sql("""
            UPDATE "ProjectTots"
            SET "StartDatePrecision" = CASE
                    WHEN "StartedOn" IS NULL THEN 0
                    WHEN EXTRACT(MONTH FROM "StartedOn") = 1
                         AND EXTRACT(DAY FROM "StartedOn") = 1 THEN 1
                    WHEN EXTRACT(DAY FROM "StartedOn") = 1 THEN 2
                    ELSE 3
                END,
                "CompletionDatePrecision" = CASE
                    WHEN "CompletedOn" IS NULL THEN 0
                    WHEN EXTRACT(MONTH FROM "CompletedOn") = 12
                         AND EXTRACT(DAY FROM "CompletedOn") = 31 THEN 1
                    WHEN "CompletedOn" =
                         (date_trunc('month', "CompletedOn") + interval '1 month - 1 day')::date THEN 2
                    ELSE 3
                END;
            """);

        migrationBuilder.Sql("""
            UPDATE "ProjectTotRequests"
            SET "ProposedStartDatePrecision" = CASE
                    WHEN "ProposedStartedOn" IS NULL THEN 0
                    WHEN EXTRACT(MONTH FROM "ProposedStartedOn") = 1
                         AND EXTRACT(DAY FROM "ProposedStartedOn") = 1 THEN 1
                    WHEN EXTRACT(DAY FROM "ProposedStartedOn") = 1 THEN 2
                    ELSE 3
                END,
                "ProposedCompletionDatePrecision" = CASE
                    WHEN "ProposedCompletedOn" IS NULL THEN 0
                    WHEN EXTRACT(MONTH FROM "ProposedCompletedOn") = 12
                         AND EXTRACT(DAY FROM "ProposedCompletedOn") = 31 THEN 1
                    WHEN "ProposedCompletedOn" =
                         (date_trunc('month', "ProposedCompletedOn") + interval '1 month - 1 day')::date THEN 2
                    ELSE 3
                END;
            """);

        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTots_StartDatePrecision", table: "ProjectTots", sql: "\"StartDatePrecision\" BETWEEN 0 AND 3");
        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTots_CompletionDatePrecision", table: "ProjectTots", sql: "\"CompletionDatePrecision\" BETWEEN 0 AND 3");
        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTotRequests_StartDatePrecision", table: "ProjectTotRequests", sql: "\"ProposedStartDatePrecision\" BETWEEN 0 AND 3");
        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTotRequests_CompletionDatePrecision", table: "ProjectTotRequests", sql: "\"ProposedCompletionDatePrecision\" BETWEEN 0 AND 3");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTots_StartDatePrecision", table: "ProjectTots");
        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTots_CompletionDatePrecision", table: "ProjectTots");
        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTotRequests_StartDatePrecision", table: "ProjectTotRequests");
        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTotRequests_CompletionDatePrecision", table: "ProjectTotRequests");
        migrationBuilder.DropColumn(name: "StartDatePrecision", table: "ProjectTots");
        migrationBuilder.DropColumn(name: "CompletionDatePrecision", table: "ProjectTots");
        migrationBuilder.DropColumn(name: "ProposedStartDatePrecision", table: "ProjectTotRequests");
        migrationBuilder.DropColumn(name: "ProposedCompletionDatePrecision", table: "ProjectTotRequests");
    }
}
