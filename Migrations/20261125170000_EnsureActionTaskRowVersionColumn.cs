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

        // SECTION: Preserve ActionTasks optimistic concurrency column on rollback
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: Non-destructive rollback
            // This repair migration may be a no-op when RowVersion was already created by
            // 20260505130000_AddActionTaskRowVersionConcurrency. Dropping the column here
            // would break the model after rolling back only this repair migration.
        }
    }
}
