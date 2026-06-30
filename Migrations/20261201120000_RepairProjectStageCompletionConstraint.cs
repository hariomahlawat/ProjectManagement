using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201120000_RepairProjectStageCompletionConstraint")]
    public partial class RepairProjectStageCompletionConstraint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""ProjectStages""
    ALTER COLUMN ""ActualStart"" DROP NOT NULL;

ALTER TABLE ""ProjectStages""
    ALTER COLUMN ""CompletedOn"" DROP NOT NULL;

UPDATE ""ProjectStages""
SET ""RequiresBackfill"" = TRUE
WHERE ""Status"" = 'Completed'
  AND ""CompletedOn"" IS NULL
  AND COALESCE(""RequiresBackfill"", FALSE) = FALSE;

ALTER TABLE ""ProjectStages""
    DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";

ALTER TABLE ""ProjectStages""
    ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
    CHECK (
        ""Status"" <> 'Completed'
        OR ""CompletedOn"" IS NOT NULL
        OR ""RequiresBackfill"" IS TRUE
    );");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
ALTER TABLE [ProjectStages] ALTER COLUMN [ActualStart] date NULL;
ALTER TABLE [ProjectStages] ALTER COLUMN [CompletedOn] date NULL;

UPDATE [ProjectStages]
SET [RequiresBackfill] = 1
WHERE [Status] = 'Completed'
  AND [CompletedOn] IS NULL
  AND ISNULL([RequiresBackfill], 0) = 0;

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_ProjectStages_CompletedHasDate'
      AND parent_object_id = OBJECT_ID(N'[ProjectStages]'))
BEGIN
    ALTER TABLE [ProjectStages]
        DROP CONSTRAINT [CK_ProjectStages_CompletedHasDate];
END;

ALTER TABLE [ProjectStages] WITH CHECK
    ADD CONSTRAINT [CK_ProjectStages_CompletedHasDate]
    CHECK (
        [Status] <> 'Completed'
        OR [CompletedOn] IS NOT NULL
        OR [RequiresBackfill] = 1
    );

ALTER TABLE [ProjectStages]
    CHECK CONSTRAINT [CK_ProjectStages_CompletedHasDate];");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite requires a table rebuild to alter a check constraint. The deployed
                // provider is PostgreSQL; SQLite is used only for lightweight tests.
            }
            else
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""ProjectStages""
    DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";

ALTER TABLE ""ProjectStages""
    ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
    CHECK (
        ""Status"" <> 'Completed'
        OR ""CompletedOn"" IS NOT NULL
        OR ""RequiresBackfill"" IS TRUE
    );");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
UPDATE ""ProjectStages""
SET ""RequiresBackfill"" = TRUE
WHERE ""Status"" = 'Completed'
  AND (
      ""CompletedOn"" IS NULL
      OR ""ActualStart"" IS NULL
  )
  AND COALESCE(""RequiresBackfill"", FALSE) = FALSE;

ALTER TABLE ""ProjectStages""
    DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";

ALTER TABLE ""ProjectStages""
    ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
    CHECK (
        ""Status"" <> 'Completed'
        OR (
            ""CompletedOn"" IS NOT NULL
            AND ""ActualStart"" IS NOT NULL
        )
        OR ""RequiresBackfill"" IS TRUE
    );");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
UPDATE [ProjectStages]
SET [RequiresBackfill] = 1
WHERE [Status] = 'Completed'
  AND (
      [CompletedOn] IS NULL
      OR [ActualStart] IS NULL
  )
  AND ISNULL([RequiresBackfill], 0) = 0;

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_ProjectStages_CompletedHasDate'
      AND parent_object_id = OBJECT_ID(N'[ProjectStages]'))
BEGIN
    ALTER TABLE [ProjectStages]
        DROP CONSTRAINT [CK_ProjectStages_CompletedHasDate];
END;

ALTER TABLE [ProjectStages] WITH CHECK
    ADD CONSTRAINT [CK_ProjectStages_CompletedHasDate]
    CHECK (
        [Status] <> 'Completed'
        OR (
            [CompletedOn] IS NOT NULL
            AND [ActualStart] IS NOT NULL
        )
        OR [RequiresBackfill] = 1
    );");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // See Up().
            }
            else
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""ProjectStages""
    DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";

ALTER TABLE ""ProjectStages""
    ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
    CHECK (
        ""Status"" <> 'Completed'
        OR (
            ""CompletedOn"" IS NOT NULL
            AND ""ActualStart"" IS NOT NULL
        )
        OR ""RequiresBackfill"" IS TRUE
    );");
            }
        }
    }
}
