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
            // SECTION: Data remediation policy for workflow version shrink
            // Policy: this migration does not mutate unknown legacy values. It fails fast with
            // a clear exception when invalid data exists so operators can remediate deterministically.
            migrationBuilder.Sql(
                @"DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ""Projects""
        WHERE ""WorkflowVersion"" IS NULL
           OR length(""WorkflowVersion"") > 32
    ) THEN
        RAISE EXCEPTION
            'Cannot shrink Projects.WorkflowVersion to varchar(32): found NULL or values longer than 32 characters. Remediate data before applying migration.';
    END IF;
END $$;");

            // SECTION: Enforce final schema after data is validated
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

        // SECTION: Revert schema changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: Roll back schema change to previous length/constraint contract
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
    }
}
