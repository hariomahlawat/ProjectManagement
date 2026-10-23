using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Chat storage management (docs/chat-attachments.md): why and by whom an attachment was
    /// removed, and the batch it was removed in (so a clean-up can be undone within a day). Three
    /// nullable columns and an index on ChatAttachments; existing rows keep NULL. Pre-existing
    /// model/snapshot drift elsewhere is deliberately not included, as in the earlier chat
    /// migrations.
    /// </summary>
    public partial class AddChatAttachmentRemovalDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RemovalBatchId",
                table: "ChatAttachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemovalReason",
                table: "ChatAttachments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemovedByUserId",
                table: "ChatAttachments",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_RemovalBatchId",
                table: "ChatAttachments",
                column: "RemovalBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatAttachments_RemovalBatchId",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "RemovalBatchId",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "RemovalReason",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "RemovedByUserId",
                table: "ChatAttachments");
        }
    }
}
