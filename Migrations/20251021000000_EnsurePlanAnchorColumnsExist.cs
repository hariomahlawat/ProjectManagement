using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class EnsurePlanAnchorColumnsExist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"AnchorDate\" date;");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"AnchorStageCode\" character varying(16);");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"SkipWeekends\" boolean NOT NULL DEFAULT true;");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"TransitionRule\" character varying(32) NOT NULL DEFAULT 'NextWorkingDay';");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"PncApplicable\" boolean NOT NULL DEFAULT true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"AnchorDate\";");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"AnchorStageCode\";");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"SkipWeekends\";");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"TransitionRule\";");
            migrationBuilder.Sql(@"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"PncApplicable\";");
        }
    }
}
