using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260926130000_AddProjectWorkflowVersion")]
    public partial class AddProjectWorkflowVersion : Migration
    {
        // SECTION: Apply schema changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkflowVersion",
                table: "Projects",
                type: "character varying(32)",
                nullable: false,
                defaultValue: "SDD-2.0");

            migrationBuilder.Sql(@"UPDATE \"Projects\" SET \"WorkflowVersion\" = 'SDD-2.0' WHERE \"WorkflowVersion\" IS NULL;");
        }

        // SECTION: Revert schema changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                table: "Projects");
        }
    }
}
