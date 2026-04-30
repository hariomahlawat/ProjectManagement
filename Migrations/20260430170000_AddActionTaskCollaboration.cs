using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260430170000_AddActionTaskCollaboration")]
    public class AddActionTaskCollaboration : Migration
    {
        // SECTION: Add task updates and attachments schema
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionTaskUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionTaskUpdates", x => x.Id);
                    table.ForeignKey("FK_ActionTaskUpdates_ActionTasks_TaskId", x => x.TaskId, "ActionTasks", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionTaskAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    UpdateId = table.Column<int>(type: "integer", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionTaskAttachments", x => x.Id);
                    table.ForeignKey("FK_ActionTaskAttachments_ActionTasks_TaskId", x => x.TaskId, "ActionTasks", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_ActionTaskAttachments_ActionTaskUpdates_UpdateId", x => x.UpdateId, "ActionTaskUpdates", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_ActionTaskUpdates_TaskId_CreatedAtUtc", table: "ActionTaskUpdates", columns: new[] { "TaskId", "CreatedAtUtc" });
            migrationBuilder.CreateIndex(name: "IX_ActionTaskAttachments_TaskId", table: "ActionTaskAttachments", column: "TaskId");
            migrationBuilder.CreateIndex(name: "IX_ActionTaskAttachments_UpdateId", table: "ActionTaskAttachments", column: "UpdateId");
        }

        // SECTION: Remove task updates and attachments schema
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ActionTaskAttachments");
            migrationBuilder.DropTable(name: "ActionTaskUpdates");
        }
    }
}
