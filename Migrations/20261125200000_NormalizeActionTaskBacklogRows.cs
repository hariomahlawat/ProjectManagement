using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125200000_NormalizeActionTaskBacklogRows")]
    public partial class NormalizeActionTaskBacklogRows : Migration
    {
        // SECTION: Normalize legacy unassigned open no-sprint rows into true backlog records
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                || ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "ActionTasks"
                    SET "Status" = 'Backlog',
                        "SubmittedOn" = NULL,
                        "ClosedOn" = NULL
                    WHERE "IsDeleted" = FALSE
                      AND ("AssignedToUserId" IS NULL OR "AssignedToUserId" = '')
                      AND "SprintId" IS NULL
                      AND "Status" <> 'Closed';
                    """);

                // SECTION: Defensive check for invalid sprint rows retained for manual business correction
                // Rows returned by this query have a SprintId but no assignee. Do not auto-fix them here because
                // the correct responsible person needs a business decision before the sprint bucket can be trusted.
                migrationBuilder.Sql(
                    """
                    SELECT *
                    FROM "ActionTasks"
                    WHERE "IsDeleted" = FALSE
                      AND "SprintId" IS NOT NULL
                      AND ("AssignedToUserId" IS NULL OR "AssignedToUserId" = '');
                    """);
            }
            else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    UPDATE [ActionTasks]
                    SET [Status] = N'Backlog',
                        [SubmittedOn] = NULL,
                        [ClosedOn] = NULL
                    WHERE [IsDeleted] = CAST(0 AS bit)
                      AND ([AssignedToUserId] IS NULL OR [AssignedToUserId] = N'')
                      AND [SprintId] IS NULL
                      AND [Status] <> N'Closed';
                    """);

                // SECTION: Defensive check for invalid sprint rows retained for manual business correction
                // Rows returned by this query have a SprintId but no assignee. Do not auto-fix them here because
                // the correct responsible person needs a business decision before the sprint bucket can be trusted.
                migrationBuilder.Sql(
                    """
                    SELECT *
                    FROM [ActionTasks]
                    WHERE [IsDeleted] = CAST(0 AS bit)
                      AND [SprintId] IS NOT NULL
                      AND ([AssignedToUserId] IS NULL OR [AssignedToUserId] = N'');
                    """);
            }
        }

        // SECTION: Data normalization is forward-only and does not reintroduce invalid legacy states
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
