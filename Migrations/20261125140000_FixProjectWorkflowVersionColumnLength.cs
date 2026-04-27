using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125140000_FixProjectWorkflowVersionColumnLength")]
    public partial class FixProjectWorkflowVersionColumnLength : Migration
    {
        // SECTION: Apply schema changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WorkflowVersion",
                table: "Projects",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "SDD-1.0",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "SDD-1.0");
        }

        // SECTION: Revert schema changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE \"Projects\"
                SET \"WorkflowVersion\" = LEFT(\"WorkflowVersion\", 32)
                WHERE length(\"WorkflowVersion\") > 32;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowVersion",
                table: "Projects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "SDD-1.0",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "SDD-1.0");
        }
    }
}
