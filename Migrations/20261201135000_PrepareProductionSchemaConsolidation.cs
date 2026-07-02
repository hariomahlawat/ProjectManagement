using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Prepares legacy production databases for the schema-consolidation migration.
///
/// Some earlier PRISM builds recreated the obsolete ProjectStages completion check
/// constraint during startup after the repair migration had already been recorded.
/// Such databases can contain nullable RequiresBackfill values that satisfy the old
/// PostgreSQL CHECK through UNKNOWN, but fail when a later migration normalises those
/// values to FALSE. This migration removes that ordering hazard before
/// ConsolidateProductionSchemaMaintenance runs.
///
/// The operation is deliberately idempotent. It is safe both for databases that still
/// carry the legacy constraint and for databases that have already been reconciled.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201135000_PrepareProductionSchemaConsolidation")]
public sealed class PrepareProductionSchemaConsolidation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "ProjectStages"
                    ADD COLUMN IF NOT EXISTS "RequiresBackfill" boolean;

                ALTER TABLE "ProjectStages"
                    ALTER COLUMN "ActualStart" DROP NOT NULL,
                    ALTER COLUMN "CompletedOn" DROP NOT NULL;

                -- Remove any obsolete rule before converting NULL backfill markers. Under
                -- PostgreSQL CHECK semantics, a legacy row may have been accepted because
                -- the old expression evaluated to UNKNOWN; changing NULL to FALSE while the
                -- old rule remains would abort the migration.
                ALTER TABLE "ProjectStages"
                    DROP CONSTRAINT IF EXISTS "CK_ProjectStages_CompletedHasDate";

                UPDATE "ProjectStages"
                SET "RequiresBackfill" = TRUE
                WHERE "Status" = 'Completed'
                  AND "CompletedOn" IS NULL;

                UPDATE "ProjectStages"
                SET "RequiresBackfill" = FALSE
                WHERE "RequiresBackfill" IS NULL;

                ALTER TABLE "ProjectStages"
                    ALTER COLUMN "RequiresBackfill" SET DEFAULT FALSE,
                    ALTER COLUMN "RequiresBackfill" SET NOT NULL;

                ALTER TABLE "ProjectStages"
                    ADD CONSTRAINT "CK_ProjectStages_CompletedHasDate"
                    CHECK (
                        "Status" <> 'Completed'
                        OR "CompletedOn" IS NOT NULL
                        OR "RequiresBackfill" IS TRUE
                    );
                """);
        }
        else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'dbo.ProjectStages', 'RequiresBackfill') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[ProjectStages]
                        ADD [RequiresBackfill] bit NULL;
                END;

                ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [ActualStart] date NULL;
                ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [CompletedOn] date NULL;

                IF EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = 'CK_ProjectStages_CompletedHasDate'
                      AND parent_object_id = OBJECT_ID(N'[dbo].[ProjectStages]')
                )
                BEGIN
                    ALTER TABLE [dbo].[ProjectStages]
                        DROP CONSTRAINT [CK_ProjectStages_CompletedHasDate];
                END;

                UPDATE [dbo].[ProjectStages]
                SET [RequiresBackfill] = 1
                WHERE [Status] = 'Completed'
                  AND [CompletedOn] IS NULL;

                UPDATE [dbo].[ProjectStages]
                SET [RequiresBackfill] = 0
                WHERE [RequiresBackfill] IS NULL;

                ALTER TABLE [dbo].[ProjectStages]
                    ALTER COLUMN [RequiresBackfill] bit NOT NULL;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.default_constraints defaults
                    INNER JOIN sys.columns columns
                        ON columns.default_object_id = defaults.object_id
                    WHERE defaults.parent_object_id = OBJECT_ID(N'[dbo].[ProjectStages]')
                      AND columns.name = 'RequiresBackfill'
                )
                BEGIN
                    ALTER TABLE [dbo].[ProjectStages]
                        ADD CONSTRAINT [DF_ProjectStages_RequiresBackfill_Prepare]
                        DEFAULT 0 FOR [RequiresBackfill];
                END;

                ALTER TABLE [dbo].[ProjectStages] WITH CHECK
                    ADD CONSTRAINT [CK_ProjectStages_CompletedHasDate]
                    CHECK (
                        [Status] <> 'Completed'
                        OR [CompletedOn] IS NOT NULL
                        OR [RequiresBackfill] = 1
                    );

                ALTER TABLE [dbo].[ProjectStages]
                    CHECK CONSTRAINT [CK_ProjectStages_CompletedHasDate];
                """);
        }
        // SQLite test databases are created from the current model. Rebuilding the table
        // solely to alter a check constraint is unnecessary for this production repair.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally irreversible. Rolling back must not restore the obsolete completion
        // rule or nullable backfill state that this migration was created to eliminate.
    }
}
