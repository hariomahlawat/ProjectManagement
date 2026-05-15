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
        // SECTION: Normalize legacy unassigned open no-sprint rows into backlog item records
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                || ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "ActionTasks"
                    SET "Status" = 'Backlog',
                        "AssignedToRole" = '',
                        "SubmittedOn" = NULL,
                        "ClosedOn" = NULL
                    WHERE "IsDeleted" = FALSE
                      AND ("AssignedToUserId" IS NULL OR "AssignedToUserId" = '')
                      AND "SprintId" IS NULL
                      AND "Status" <> 'Closed';
                    """);

                // SECTION: Invalid sprint rows with no assignee cannot remain normal sprint work; return them to Backlog explicitly.
                migrationBuilder.Sql(
                    """
                    UPDATE "ActionTasks"
                    SET "Status" = 'Backlog',
                        "SprintId" = NULL,
                        "AssignedToRole" = '',
                        "SubmittedOn" = NULL,
                        "ClosedOn" = NULL
                    WHERE "IsDeleted" = FALSE
                      AND "SprintId" IS NOT NULL
                      AND ("AssignedToUserId" IS NULL OR "AssignedToUserId" = '')
                      AND "Status" <> 'Closed';
                    """);
            }
            else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    UPDATE [ActionTasks]
                    SET [Status] = N'Backlog',
                        [AssignedToRole] = N'',
                        [SubmittedOn] = NULL,
                        [ClosedOn] = NULL
                    WHERE [IsDeleted] = CAST(0 AS bit)
                      AND ([AssignedToUserId] IS NULL OR [AssignedToUserId] = N'')
                      AND [SprintId] IS NULL
                      AND [Status] <> N'Closed';
                    """);

                // SECTION: Invalid sprint rows with no assignee cannot remain normal sprint work; return them to Backlog explicitly.
                migrationBuilder.Sql(
                    """
                    UPDATE [ActionTasks]
                    SET [Status] = N'Backlog',
                        [SprintId] = NULL,
                        [AssignedToRole] = N'',
                        [SubmittedOn] = NULL,
                        [ClosedOn] = NULL
                    WHERE [IsDeleted] = CAST(0 AS bit)
                      AND [SprintId] IS NOT NULL
                      AND ([AssignedToUserId] IS NULL OR [AssignedToUserId] = N'')
                      AND [Status] <> N'Closed';
                    """);
            }
        }

        // SECTION: Data normalization is forward-only and does not reintroduce invalid legacy states
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
