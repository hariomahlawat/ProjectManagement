using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125233000_ExpandNotebookChecklistText")]
    public partial class ExpandNotebookChecklistText : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SECTION: Align checklist item text length with application validation
            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "NotebookChecklistItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: Restore previous checklist item text length
            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "NotebookChecklistItems",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);
        }
    }
}
