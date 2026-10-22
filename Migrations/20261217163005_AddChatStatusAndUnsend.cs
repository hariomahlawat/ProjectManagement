using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Chat: status messages (new ChatUserStatuses table) and unsend (nullable
    /// ChatMessages.UnsentUtc, with a check that an unsent message has no text). Additive only;
    /// existing messages have UnsentUtc = NULL and pass the check. Pre-existing model/snapshot
    /// drift elsewhere (NotebookItemCollaborators, Projects.ProjectBrief length, an index on
    /// ProjectIdeaComments) is deliberately not included.
    /// </summary>
    public partial class AddChatStatusAndUnsend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UnsentUtc",
                table: "ChatMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatUserStatuses",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Message = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatUserStatuses", x => x.UserId);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatMessages_UnsentHasNoBody",
                table: "ChatMessages",
                sql: "\"UnsentUtc\" IS NULL OR \"Body\" = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatMessages_UnsentHasNoBody",
                table: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatUserStatuses");

            migrationBuilder.DropColumn(
                name: "UnsentUtc",
                table: "ChatMessages");
        }
    }
}
