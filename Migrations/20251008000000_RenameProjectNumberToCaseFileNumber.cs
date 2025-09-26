using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class RenameProjectNumberToCaseFileNumber : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_ProjectNumber",
                table: "Projects");

            migrationBuilder.RenameColumn(
                name: "ProjectNumber",
                table: "Projects",
                newName: "CaseFileNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CaseFileNumber",
                table: "Projects",
                column: "CaseFileNumber",
                unique: true,
                filter: "\"CaseFileNumber\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_CaseFileNumber",
                table: "Projects");

            migrationBuilder.RenameColumn(
                name: "CaseFileNumber",
                table: "Projects",
                newName: "ProjectNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ProjectNumber",
                table: "Projects",
                column: "ProjectNumber",
                unique: true,
                filter: "\"ProjectNumber\" IS NOT NULL");
        }
    }
}
