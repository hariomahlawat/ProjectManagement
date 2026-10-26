using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Allows only one approved annual proliferation row per project, source and year.
    /// Live data may already hold duplicates (Deployment/ProliferationDataQualityAudit.sql,
    /// query 3), and a failing CREATE UNIQUE INDEX would stop the startup migration gate. So
    /// the index is created only when the data complies; otherwise the migration logs a
    /// warning and the service-level duplicate check keeps guarding until the duplicates are
    /// resolved and the index is created with the statement in that audit script.
    /// </summary>
    public partial class AddProliferationApprovedYearIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "ProliferationYearly"
                        WHERE "ApprovalStatus" = 1
                        GROUP BY "ProjectId", "Source", "Year"
                        HAVING COUNT(*) > 1)
                    THEN
                        RAISE WARNING 'UX_ProlifYearly_ApprovedYear was not created: duplicate approved annual proliferation rows exist. Resolve them (Deployment/ProliferationDataQualityAudit.sql, query 3) and create the index with the statement at the end of that script.';
                    ELSE
                        CREATE UNIQUE INDEX IF NOT EXISTS "UX_ProlifYearly_ApprovedYear"
                            ON "ProliferationYearly" ("ProjectId", "Source", "Year")
                            WHERE "ApprovalStatus" = 1;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "UX_ProlifYearly_ApprovedYear";""");
        }
    }
}
