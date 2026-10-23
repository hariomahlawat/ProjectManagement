#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Group chats. Adds ChatConversations.Title and ManagerUserId and ChatMessages.EventKind and
    /// EventData (group notices), widens the conversation kind check to allow 'Group', and adds
    /// checks that a direct conversation has a pair key and no name, a group a name and no pair
    /// key, and that a notice has a known kind and no text. Additive only: existing rows are all
    /// direct conversations and plain messages, which pass every new check.
    /// Pre-existing model/snapshot drift elsewhere (NotebookItemCollaborators, Projects.ProjectBrief
    /// length, an index on ProjectIdeaComments) is deliberately not included.
    /// </summary>
    public partial class AddChatGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ChatConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerUserId",
                table: "ChatConversations",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventKind",
                table: "ChatMessages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventData",
                table: "ChatMessages",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatConversations_Kind",
                table: "ChatConversations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatConversations_Kind",
                table: "ChatConversations",
                sql: "\"Kind\" IN ('Direct','Group')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatConversations_Shape",
                table: "ChatConversations",
                sql: "(\"Kind\" = 'Direct' AND \"DirectKey\" IS NOT NULL AND \"Title\" IS NULL) OR (\"Kind\" = 'Group' AND \"DirectKey\" IS NULL AND \"Title\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatMessages_Event",
                table: "ChatMessages",
                sql: "\"EventKind\" IS NULL OR (\"EventKind\" IN ('Created','Added','Removed','Left','Renamed') AND \"Body\" = '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatMessages_Event",
                table: "ChatMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatConversations_Shape",
                table: "ChatConversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatConversations_Kind",
                table: "ChatConversations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatConversations_Kind",
                table: "ChatConversations",
                sql: "\"Kind\" IN ('Direct')");

            migrationBuilder.DropColumn(
                name: "EventData",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "EventKind",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ManagerUserId",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ChatConversations");
        }
    }
}
