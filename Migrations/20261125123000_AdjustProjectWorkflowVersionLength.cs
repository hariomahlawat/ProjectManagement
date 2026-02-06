using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125123000_AdjustProjectWorkflowVersionLength")]
    public partial class AdjustProjectWorkflowVersionLength : Migration
    {
        // SECTION: Apply schema changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SECTION: Align persisted schema with EF model contract
            // The model defines WorkflowVersion as MaxLength(64). Previous migration history
            // may leave the physical column at varchar(32), so this migration expands to 64.
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
            // SECTION: Roll back schema change to previous length/constraint contract
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
