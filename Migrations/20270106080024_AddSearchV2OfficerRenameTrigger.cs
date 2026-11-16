using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Search V2 (docs/ux/search-review-6-fixes.md, PR 4): IT, MOLTI and SRE results carry the
    /// Project Officer's rank and name, so a change to either queues that officer's projects
    /// for re-indexing instead of waiting for the nightly rebuild. Additive: one function and
    /// one row trigger on "AspNetUsers" that fires only when "FullName" or "Rank" changes, and
    /// only inserts work items through the existing <c>search_v2_enqueue</c>. No table or data
    /// changes. Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270106080024_AddSearchV2OfficerRenameTrigger")]
    public partial class AddSearchV2OfficerRenameTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                return;
            }

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION search_v2_enqueue_officer_it_projects()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    project_id integer;
                BEGIN
                    FOR project_id IN
                        SELECT "Id" FROM "ItProjects"
                        WHERE "ProjectOfficerUserId" = NEW."Id" AND NOT "IsDeleted"
                    LOOP
                        PERFORM search_v2_enqueue('ItProject', project_id::text);
                    END LOOP;
                    RETURN NEW;
                END;
                $$;

                DROP TRIGGER IF EXISTS "TR_SearchV2_OfficerItProjects" ON "AspNetUsers";
                CREATE TRIGGER "TR_SearchV2_OfficerItProjects"
                    AFTER UPDATE OF "FullName", "Rank" ON "AspNetUsers"
                    FOR EACH ROW
                    WHEN (OLD."FullName" IS DISTINCT FROM NEW."FullName" OR OLD."Rank" IS DISTINCT FROM NEW."Rank")
                    EXECUTE FUNCTION search_v2_enqueue_officer_it_projects();
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
                DROP TRIGGER IF EXISTS "TR_SearchV2_OfficerItProjects" ON "AspNetUsers";
                DROP FUNCTION IF EXISTS search_v2_enqueue_officer_it_projects();
                """);
        }
    }
}
