using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Chat attachments (docs/chat-attachments.md): a new ChatAttachments table only. Additive;
    /// nothing existing changes, and the feature stays off (Chat:Attachments:Enabled) until it is
    /// switched on. Pre-existing model/snapshot drift elsewhere (NotebookItemCollaborators,
    /// Projects.ProjectBrief length, an index on ProjectIdeaComments) is deliberately not
    /// included, as in the earlier chat migrations.
    /// </summary>
    public partial class AddChatAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UploaderUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ThumbnailKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PurgedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAttachments", x => x.Id);
                    table.CheckConstraint("CK_ChatAttachments_Size", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_ChatAttachments_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_MessageId",
                table: "ChatAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_RemovedUtc_PurgedUtc",
                table: "ChatAttachments",
                columns: new[] { "RemovedUtc", "PurgedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_StorageKey",
                table: "ChatAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_UploaderUserId_RemovedUtc",
                table: "ChatAttachments",
                columns: new[] { "UploaderUserId", "RemovedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatAttachments");
        }
    }
}
