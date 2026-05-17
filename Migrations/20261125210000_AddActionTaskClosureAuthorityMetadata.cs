using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125210000_AddActionTaskClosureAuthorityMetadata")]
    public partial class AddActionTaskClosureAuthorityMetadata : Migration
    {
        // SECTION: Add command-closure metadata fields to action tasks.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClosedByUserId",
                table: "ActionTasks",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureRemarks",
                table: "ActionTasks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        // SECTION: Remove command-closure metadata fields from action tasks.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "ActionTasks");

            migrationBuilder.DropColumn(
                name: "ClosureRemarks",
                table: "ActionTasks");
        }
    }
}
