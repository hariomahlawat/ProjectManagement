using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Chat Phase 3 (docs/chat.md): replies, edits, reactions and @mentions. Additive only: two
    /// nullable columns on "ChatMessages" ("ReplyToMessageId", "EditedUtc"), null for every
    /// existing message, and two new tables, "ChatMessageReactions" and "ChatMessageMentions",
    /// each removed with its message. No existing row changes. The previous build does not start
    /// against the migrated database (startup refuses a migration it does not know): going back
    /// follows the rollback SOP, a database restore or this migration's Down, which drops the
    /// reactions, mentions and the reply/edit marks (message text is untouched). Hand-written,
    /// with the model snapshot updated for the chat tables (ChatModelSnapshotTests).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270108080026_AddChatRepliesReactionsMentions")]
    public partial class AddChatRepliesReactionsMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReplyToMessageId",
                table: "ChatMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditedUtc",
                table: "ChatMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatMessageReactions",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageReactions", x => new { x.MessageId, x.UserId, x.Kind });
                    table.CheckConstraint("CK_ChatMessageReactions_Kind", "\"Kind\" IN ('like','noted','looking')");
                    table.ForeignKey(
                        name: "FK_ChatMessageReactions_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageMentions",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageMentions", x => new { x.MessageId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChatMessageMentions_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChatMessageMentions");

            migrationBuilder.DropTable(name: "ChatMessageReactions");

            migrationBuilder.DropColumn(name: "EditedUtc", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "ReplyToMessageId", table: "ChatMessages");
        }
    }
}
