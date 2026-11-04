using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Makes deleting a shared briefing deck recoverable: a deleted deck keeps its row with
    /// DeletedAtUtc set and can be restored for a retention window. Additive: both columns are
    /// nullable and every existing deck stays live. Deck names stay unique among live decks only,
    /// so the unique name index becomes a partial index (no existing row can violate it, since
    /// it was unique over all rows before).
    /// </remarks>
    public partial class AddProjectBriefingDeckSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "ProjectBriefingDecks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "ProjectBriefingDecks",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.DropIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks",
                column: "NormalizedName",
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBriefingDecks_DeletedAtUtc",
                table: "ProjectBriefingDecks",
                column: "DeletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBriefingDecks_DeletedByUserId",
                table: "ProjectBriefingDecks",
                column: "DeletedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectBriefingDecks_AspNetUsers_DeletedByUserId",
                table: "ProjectBriefingDecks",
                column: "DeletedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Rolling back removes decks still waiting in the deleted list (they were deleted by a
        /// user) so the full unique name index can be restored.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"ProjectBriefingDecks\" WHERE \"DeletedAtUtc\" IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectBriefingDecks_AspNetUsers_DeletedByUserId",
                table: "ProjectBriefingDecks");

            migrationBuilder.DropIndex(
                name: "IX_ProjectBriefingDecks_DeletedByUserId",
                table: "ProjectBriefingDecks");

            migrationBuilder.DropIndex(
                name: "IX_ProjectBriefingDecks_DeletedAtUtc",
                table: "ProjectBriefingDecks");

            migrationBuilder.DropIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ProjectBriefingDecks");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "ProjectBriefingDecks");
        }
    }
}
