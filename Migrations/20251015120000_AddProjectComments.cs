using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ProjectStageId = table.Column<int>(type: "integer", nullable: true),
                    ParentCommentId = table.Column<int>(type: "integer", nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Pinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    EditedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    EditedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectComments_ProjectComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "ProjectComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectComments_ProjectStages_ProjectStageId",
                        column: x => x.ProjectStageId,
                        principalTable: "ProjectStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectComments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCommentAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCommentAttachments_ProjectComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "ProjectComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCommentMentions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommentMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCommentMentions_ProjectComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "ProjectComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectCommentMentions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommentAttachments_CommentId",
                table: "ProjectCommentAttachments",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommentMentions_CommentId_UserId",
                table: "ProjectCommentMentions",
                columns: new[] { "CommentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommentMentions_UserId",
                table: "ProjectCommentMentions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_ParentCommentId",
                table: "ProjectComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_ProjectId_CreatedOn",
                table: "ProjectComments",
                columns: new[] { "ProjectId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_ProjectStageId",
                table: "ProjectComments",
                column: "ProjectStageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectCommentAttachments");

            migrationBuilder.DropTable(
                name: "ProjectCommentMentions");

            migrationBuilder.DropTable(
                name: "ProjectComments");
        }
    }
}
