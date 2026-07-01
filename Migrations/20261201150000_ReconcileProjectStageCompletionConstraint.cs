using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Reconciles production databases where legacy startup SQL recreated the obsolete
    /// completion constraint after the earlier repair migration had already been recorded.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201150000_ReconcileProjectStageCompletionConstraint")]
    public partial class ReconcileProjectStageCompletionConstraint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "ProjectStages"
                        ADD COLUMN IF NOT EXISTS "RequiresBackfill" boolean NOT NULL DEFAULT FALSE;

                    ALTER TABLE "ProjectStages"
                        ALTER COLUMN "ActualStart" DROP NOT NULL,
                        ALTER COLUMN "CompletedOn" DROP NOT NULL,
                        ALTER COLUMN "RequiresBackfill" SET DEFAULT FALSE;

                    -- Drop the legacy rule before normalising nullable legacy rows. PostgreSQL
                    -- check constraints accept UNKNOWN, so an old row may contain NULL here
                    -- even though assigning FALSE would fail the obsolete rule mid-migration.
                    ALTER TABLE "ProjectStages"
                        DROP CONSTRAINT IF EXISTS "CK_ProjectStages_CompletedHasDate";

                    UPDATE "ProjectStages"
                    SET "RequiresBackfill" = FALSE
                    WHERE "RequiresBackfill" IS NULL;

                    UPDATE "ProjectStages"
                    SET "RequiresBackfill" = TRUE
                    WHERE "Status" = 'Completed'
                      AND "CompletedOn" IS NULL
                      AND "RequiresBackfill" = FALSE;

                    ALTER TABLE "ProjectStages"
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
                        ALTER TABLE [dbo].[ProjectStages]
                            ADD [RequiresBackfill] bit NOT NULL
                            CONSTRAINT [DF_ProjectStages_RequiresBackfill_Reconcile] DEFAULT 0;

                    ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [ActualStart] date NULL;
                    ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [CompletedOn] date NULL;

                    IF EXISTS (
                        SELECT 1
                        FROM sys.check_constraints
                        WHERE name = 'CK_ProjectStages_CompletedHasDate'
                          AND parent_object_id = OBJECT_ID(N'[dbo].[ProjectStages]'))
                    BEGIN
                        ALTER TABLE [dbo].[ProjectStages]
                            DROP CONSTRAINT [CK_ProjectStages_CompletedHasDate];
                    END;

                    UPDATE [dbo].[ProjectStages]
                    SET [RequiresBackfill] = 0
                    WHERE [RequiresBackfill] IS NULL;

                    ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [RequiresBackfill] bit NOT NULL;

                    UPDATE [dbo].[ProjectStages]
                    SET [RequiresBackfill] = 1
                    WHERE [Status] = 'Completed'
                      AND [CompletedOn] IS NULL
                      AND [RequiresBackfill] = 0;

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
            // SQLite test databases are created from the current EF model. Rebuilding a
            // production table solely to modify a check constraint is unnecessary there.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible. Rolling back must not restore the obsolete rule
            // that required ActualStart for every completed stage.
        }
    }
}
