using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Final forward repair for databases where legacy application startup code recreated
    /// the obsolete completion constraint after the earlier repair migration was recorded.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201160000_FinalizeProjectStageCompletionConstraint")]
    public partial class FinalizeProjectStageCompletionConstraint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "ProjectStages"
                        ADD COLUMN IF NOT EXISTS "RequiresBackfill" boolean NOT NULL DEFAULT FALSE;

                    -- Remove the obsolete rule before normalising legacy rows. This permits
                    -- completed stages with a known CompletedOn date and no ActualStart date.
                    ALTER TABLE "ProjectStages"
                        DROP CONSTRAINT IF EXISTS "CK_ProjectStages_CompletedHasDate";

                    UPDATE "ProjectStages"
                    SET "RequiresBackfill" = FALSE
                    WHERE "RequiresBackfill" IS NULL;

                    UPDATE "ProjectStages"
                    SET "RequiresBackfill" = TRUE
                    WHERE "Status" = 'Completed'
                      AND "CompletedOn" IS NULL
                      AND COALESCE("RequiresBackfill", FALSE) = FALSE;

                    ALTER TABLE "ProjectStages"
                        ALTER COLUMN "ActualStart" DROP NOT NULL,
                        ALTER COLUMN "CompletedOn" DROP NOT NULL,
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
                            ADD [RequiresBackfill] bit NOT NULL
                            CONSTRAINT [DF_ProjectStages_RequiresBackfill_Final] DEFAULT 0;
                    END;

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

                    UPDATE [dbo].[ProjectStages]
                    SET [RequiresBackfill] = 1
                    WHERE [Status] = N'Completed'
                      AND [CompletedOn] IS NULL
                      AND ISNULL([RequiresBackfill], 0) = 0;

                    ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [ActualStart] date NULL;
                    ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [CompletedOn] date NULL;
                    ALTER TABLE [dbo].[ProjectStages] ALTER COLUMN [RequiresBackfill] bit NOT NULL;

                    ALTER TABLE [dbo].[ProjectStages] WITH CHECK
                        ADD CONSTRAINT [CK_ProjectStages_CompletedHasDate]
                        CHECK (
                            [Status] <> N'Completed'
                            OR [CompletedOn] IS NOT NULL
                            OR [RequiresBackfill] = 1
                        );

                    ALTER TABLE [dbo].[ProjectStages]
                        CHECK CONSTRAINT [CK_ProjectStages_CompletedHasDate];
                    """);
            }
            // SQLite test databases are created from the current EF model. Rebuilding the
            // table solely to alter a production check constraint is unnecessary there.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible. A rollback must not restore the obsolete rule
            // that required ActualStart for every completed stage.
        }
    }
}
