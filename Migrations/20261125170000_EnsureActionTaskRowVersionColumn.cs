using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125170000_EnsureActionTaskRowVersionColumn")]
    public class EnsureActionTaskRowVersionColumn : Migration
    {
        // SECTION: Ensure ActionTasks optimistic concurrency column exists
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SECTION: PostgreSQL idempotent RowVersion repair
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "ActionTasks"
                    ADD COLUMN IF NOT EXISTS "RowVersion" bytea NOT NULL
                    DEFAULT decode(md5(random()::text || clock_timestamp()::text), 'hex');
                    """);

                return;
            }

            // SECTION: SQLite already receives RowVersion from the original concurrency migration
            if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // SECTION: SQL Server idempotent RowVersion repair
            if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH(N'ActionTasks', N'RowVersion') IS NULL
                    BEGIN
                        ALTER TABLE [ActionTasks]
                        ADD [RowVersion] varbinary(max) NOT NULL
                        CONSTRAINT [DF_ActionTasks_RowVersion] DEFAULT 0x;
                    END
                    """);

                return;
            }

            // SECTION: Provider-neutral RowVersion repair
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ActionTasks",
                type: "bytea",
                nullable: false,
                defaultValue: Array.Empty<byte>());
        }

        // SECTION: Remove ActionTasks optimistic concurrency column
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: PostgreSQL idempotent RowVersion rollback
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "ActionTasks"
                    DROP COLUMN IF EXISTS "RowVersion";
                    """);

                return;
            }

            // SECTION: SQLite rollback leaves the original concurrency migration untouched
            if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // SECTION: SQL Server idempotent RowVersion rollback
            if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH(N'ActionTasks', N'RowVersion') IS NOT NULL
                    BEGIN
                        DECLARE @ConstraintName nvarchar(200);

                        SELECT @ConstraintName = dc.name
                        FROM sys.default_constraints dc
                        INNER JOIN sys.columns c
                            ON c.default_object_id = dc.object_id
                        INNER JOIN sys.tables t
                            ON t.object_id = c.object_id
                        WHERE t.name = N'ActionTasks'
                            AND c.name = N'RowVersion';

                        IF @ConstraintName IS NOT NULL
                        BEGIN
                            EXEC(N'ALTER TABLE [ActionTasks] DROP CONSTRAINT [' + @ConstraintName + N']');
                        END

                        ALTER TABLE [ActionTasks] DROP COLUMN [RowVersion];
                    END
                    """);

                return;
            }

            // SECTION: Provider-neutral RowVersion rollback
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ActionTasks");
        }
    }
}
