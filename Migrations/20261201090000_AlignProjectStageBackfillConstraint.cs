using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201090000_AlignProjectStageBackfillConstraint")]
    public partial class AlignProjectStageBackfillConstraint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""ProjectStages"" DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";
ALTER TABLE ""ProjectStages"" ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
CHECK (""Status"" <> 'Completed' OR ""CompletedOn"" IS NOT NULL OR ""RequiresBackfill"" IS TRUE);");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ProjectStages_CompletedHasDate')
    ALTER TABLE [ProjectStages] DROP CONSTRAINT [CK_ProjectStages_CompletedHasDate];
ALTER TABLE [ProjectStages] WITH CHECK ADD CONSTRAINT [CK_ProjectStages_CompletedHasDate]
CHECK ([Status] <> 'Completed' OR [CompletedOn] IS NOT NULL OR [RequiresBackfill] = 1);");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite cannot alter a check constraint without rebuilding the table.
            }
            else
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""ProjectStages"" DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";
ALTER TABLE ""ProjectStages"" ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
CHECK (""Status"" <> 'Completed' OR ""CompletedOn"" IS NOT NULL OR ""RequiresBackfill"" IS TRUE);");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""ProjectStages"" DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";
ALTER TABLE ""ProjectStages"" ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
CHECK (""Status"" <> 'Completed' OR (""CompletedOn"" IS NOT NULL AND ""ActualStart"" IS NOT NULL) OR ""RequiresBackfill"" IS TRUE);");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ProjectStages_CompletedHasDate')
    ALTER TABLE [ProjectStages] DROP CONSTRAINT [CK_ProjectStages_CompletedHasDate];
ALTER TABLE [ProjectStages] WITH CHECK ADD CONSTRAINT [CK_ProjectStages_CompletedHasDate]
CHECK ([Status] <> 'Completed' OR ([CompletedOn] IS NOT NULL AND [ActualStart] IS NOT NULL) OR [RequiresBackfill] = 1);");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // See Up().
            }
            else
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""ProjectStages"" DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";
ALTER TABLE ""ProjectStages"" ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
CHECK (""Status"" <> 'Completed' OR (""CompletedOn"" IS NOT NULL AND ""ActualStart"" IS NOT NULL) OR ""RequiresBackfill"" IS TRUE);");
            }
        }
    }
}
