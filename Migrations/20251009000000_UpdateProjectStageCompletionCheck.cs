using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class UpdateProjectStageCompletionCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectStages_CompletedHasDate",
                table: "ProjectStages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectStages_CompletedHasDate",
                table: "ProjectStages",
                sql: "NOT (\"Status\" = 3 AND \"CompletedOn\" IS NULL)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectStages_CompletedHasDate",
                table: "ProjectStages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectStages_CompletedHasDate",
                table: "ProjectStages",
                sql: "NOT (\"Status\" = 3 AND \"CompletedOn\" IS NULL)");
        }
    }
}
