using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251009000000_UpdateProjectStageCompletionCheck")]
    public partial class UpdateProjectStageCompletionCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"ALTER TABLE \"ProjectStages\" DROP CONSTRAINT IF EXISTS \"CK_ProjectStages_CompletedHasDate\";");

            migrationBuilder.Sql($"ALTER TABLE \"ProjectStages\" ADD CONSTRAINT \"CK_ProjectStages_CompletedHasDate\" CHECK (\"Status\" <> 'Completed' OR (\"CompletedOn\" IS NOT NULL AND \"ActualStart\" IS NOT NULL));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"ALTER TABLE \"ProjectStages\" DROP CONSTRAINT IF EXISTS \"CK_ProjectStages_CompletedHasDate\";");

            migrationBuilder.Sql($"ALTER TABLE \"ProjectStages\" ADD CONSTRAINT \"CK_ProjectStages_CompletedHasDate\" CHECK (NOT(\"Status\" = 'Completed' AND \"CompletedOn\" IS NULL));");
        }
    }
}
