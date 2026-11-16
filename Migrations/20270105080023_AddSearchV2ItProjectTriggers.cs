using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Search V2 indexes IT, MOLTI and SRE projects (docs/ux/search-review-5-fixes.md, PR 6).
    /// Additive: three row triggers that queue an "ItProject" work item when a project, one of
    /// its stages or one of its yearly reference numbers changes, through the existing
    /// <c>search_v2_enqueue_row</c> function. No table or data changes. A process of the previous
    /// build ignores those work items (they fail as unknown and are retried by the next build);
    /// it does not start against the migrated database (startup refuses a migration it does not
    /// know): going back follows the rollback SOP, a database restore or this migration's Down.
    /// Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270105080023_AddSearchV2ItProjectTriggers")]
    public partial class AddSearchV2ItProjectTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                return;
            }

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_SearchV2_ItProjects" ON "ItProjects";
                CREATE TRIGGER "TR_SearchV2_ItProjects"
                    AFTER INSERT OR UPDATE OR DELETE ON "ItProjects"
                    FOR EACH ROW EXECUTE FUNCTION search_v2_enqueue_row('ItProject', 'Id');

                DROP TRIGGER IF EXISTS "TR_SearchV2_ItProjectStages" ON "ItProjectStages";
                CREATE TRIGGER "TR_SearchV2_ItProjectStages"
                    AFTER INSERT OR UPDATE OR DELETE ON "ItProjectStages"
                    FOR EACH ROW EXECUTE FUNCTION search_v2_enqueue_row('ItProject', 'ItProjectId');

                DROP TRIGGER IF EXISTS "TR_SearchV2_ItProjectFinancialYears" ON "ItProjectFinancialYears";
                CREATE TRIGGER "TR_SearchV2_ItProjectFinancialYears"
                    AFTER INSERT OR UPDATE OR DELETE ON "ItProjectFinancialYears"
                    FOR EACH ROW EXECUTE FUNCTION search_v2_enqueue_row('ItProject', 'ItProjectId');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                return;
            }

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_SearchV2_ItProjectFinancialYears" ON "ItProjectFinancialYears";
                DROP TRIGGER IF EXISTS "TR_SearchV2_ItProjectStages" ON "ItProjectStages";
                DROP TRIGGER IF EXISTS "TR_SearchV2_ItProjects" ON "ItProjects";
                """);
        }
    }
}
