using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Adds the photograph a briefing deck shows for each project. Additive and nullable: null
    /// keeps today's behaviour (the project's cover photo). Deleting a photo clears the choice.
    /// </remarks>
    public partial class AddProjectBriefingItemPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhotoId",
                table: "ProjectBriefingDeckItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBriefingDeckItems_PhotoId",
                table: "ProjectBriefingDeckItems",
                column: "PhotoId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectBriefingDeckItems_ProjectPhotos_PhotoId",
                table: "ProjectBriefingDeckItems",
                column: "PhotoId",
                principalTable: "ProjectPhotos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectBriefingDeckItems_ProjectPhotos_PhotoId",
                table: "ProjectBriefingDeckItems");

            migrationBuilder.DropIndex(
                name: "IX_ProjectBriefingDeckItems_PhotoId",
                table: "ProjectBriefingDeckItems");

            migrationBuilder.DropColumn(
                name: "PhotoId",
                table: "ProjectBriefingDeckItems");
        }
    }
}
