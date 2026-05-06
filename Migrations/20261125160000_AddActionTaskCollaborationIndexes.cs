using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125160000_AddActionTaskCollaborationIndexes")]
    public partial class AddActionTaskCollaborationIndexes : Migration
    {
        // SECTION: Add indexes for action task update and attachment filters
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ActionTaskUpdates_CreatedByUserId",
                table: "ActionTaskUpdates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionTaskUpdates_IsDeleted",
                table: "ActionTaskUpdates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ActionTaskAttachments_IsDeleted",
                table: "ActionTaskAttachments",
                column: "IsDeleted");
        }

        // SECTION: Remove indexes for action task update and attachment filters
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActionTaskUpdates_CreatedByUserId",
                table: "ActionTaskUpdates");

            migrationBuilder.DropIndex(
                name: "IX_ActionTaskUpdates_IsDeleted",
                table: "ActionTaskUpdates");

            migrationBuilder.DropIndex(
                name: "IX_ActionTaskAttachments_IsDeleted",
                table: "ActionTaskAttachments");
        }
    }
}
