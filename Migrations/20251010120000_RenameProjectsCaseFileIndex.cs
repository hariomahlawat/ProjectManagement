using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class RenameProjectsCaseFileIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_Projects_CaseFileNumber",
                table: "Projects",
                newName: "UX_Projects_CaseFileNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "UX_Projects_CaseFileNumber",
                table: "Projects",
                newName: "IX_Projects_CaseFileNumber");
        }
    }
}
