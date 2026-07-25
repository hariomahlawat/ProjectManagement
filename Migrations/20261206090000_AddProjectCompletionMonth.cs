using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261206090000_AddProjectCompletionMonth")]
public partial class AddProjectCompletionMonth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<short>(
            name: "CompletedMonth",
            table: "Projects",
            type: "smallint",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Projects_CompletedMonth_Range",
            table: "Projects",
            sql: "\"CompletedMonth\" IS NULL OR (\"CompletedMonth\" BETWEEN 1 AND 12)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Projects_CompletedMonth_RequiresYear",
            table: "Projects",
            sql: "\"CompletedMonth\" IS NULL OR \"CompletedYear\" IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Projects_ExactCompletion_ClearsMonth",
            table: "Projects",
            sql: "\"CompletedOn\" IS NULL OR \"CompletedMonth\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Projects_CompletedMonth_Range",
            table: "Projects");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Projects_CompletedMonth_RequiresYear",
            table: "Projects");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Projects_ExactCompletion_ClearsMonth",
            table: "Projects");

        migrationBuilder.DropColumn(
            name: "CompletedMonth",
            table: "Projects");
    }
}
