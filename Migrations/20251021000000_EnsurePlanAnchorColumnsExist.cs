using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Models.Plans;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class EnsurePlanAnchorColumnsExist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"{nameof(PlanVersion.AnchorDate)}\" date;");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"{nameof(PlanVersion.AnchorStageCode)}\" character varying(16);");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"{nameof(PlanVersion.SkipWeekends)}\" boolean NOT NULL DEFAULT true;");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"{nameof(PlanVersion.TransitionRule)}\" character varying(32) NOT NULL DEFAULT 'NextWorkingDay';");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" ADD COLUMN IF NOT EXISTS \"{nameof(PlanVersion.PncApplicable)}\" boolean NOT NULL DEFAULT true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"{nameof(PlanVersion.AnchorDate)}\";");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"{nameof(PlanVersion.AnchorStageCode)}\";");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"{nameof(PlanVersion.SkipWeekends)}\";");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"{nameof(PlanVersion.TransitionRule)}\";");
            migrationBuilder.Sql($"ALTER TABLE \"PlanVersions\" DROP COLUMN IF EXISTS \"{nameof(PlanVersion.PncApplicable)}\";");
        }
    }
}
