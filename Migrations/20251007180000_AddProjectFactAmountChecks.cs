using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFactAmountChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "ProjectIpaFacts"        ADD CONSTRAINT "ck_ipafact_amount" CHECK ("IpaCost" >= 0);
ALTER TABLE "ProjectAonFacts"        ADD CONSTRAINT "ck_aonfact_amount" CHECK ("AonCost" >= 0);
ALTER TABLE "ProjectBenchmarkFacts"  ADD CONSTRAINT "ck_bmfact_amount" CHECK ("BenchmarkCost" >= 0);
ALTER TABLE "ProjectCommercialFacts" ADD CONSTRAINT "ck_l1fact_amount" CHECK ("L1Cost" >= 0);
ALTER TABLE "ProjectPncFacts"        ADD CONSTRAINT "ck_pncfact_amount" CHECK ("PncCost" >= 0);
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "ProjectIpaFacts"        DROP CONSTRAINT IF EXISTS "ck_ipafact_amount";
ALTER TABLE "ProjectAonFacts"        DROP CONSTRAINT IF EXISTS "ck_aonfact_amount";
ALTER TABLE "ProjectBenchmarkFacts"  DROP CONSTRAINT IF EXISTS "ck_bmfact_amount";
ALTER TABLE "ProjectCommercialFacts" DROP CONSTRAINT IF EXISTS "ck_l1fact_amount";
ALTER TABLE "ProjectPncFacts"        DROP CONSTRAINT IF EXISTS "ck_pncfact_amount";
""");
        }
    }
}
