using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201170000_AddConferenceRemarkFoundation")]
    public partial class AddConferenceRemarkFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommentType",
                table: "ProjectIdeaComments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByRole",
                table: "ProjectIdeaComments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusSnapshot",
                table: "ProjectIdeaComments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByRole",
                table: "ActionTaskUpdates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDateSnapshot",
                table: "ActionTaskUpdates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusSnapshot",
                table: "ActionTaskUpdates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ProjectId_Deleted_Type_CreatedAt",
                table: "Remarks",
                columns: new[] { "ProjectId", "IsDeleted", "Type", "CreatedAtUtc" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectIdeaComments_IdeaId_Deleted_Type_CreatedAt",
                table: "ProjectIdeaComments",
                columns: new[] { "ProjectIdeaId", "IsDeleted", "CommentType", "CreatedAt" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTaskUpdates_TaskId_IsDeleted_UpdateType_CreatedAtUtc",
                table: "ActionTaskUpdates",
                columns: new[] { "TaskId", "IsDeleted", "UpdateType", "CreatedAtUtc" },
                descending: new[] { false, false, false, true });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Remarks_ProjectId_Deleted_Type_CreatedAt",
                table: "Remarks");

            migrationBuilder.DropIndex(
                name: "IX_ProjectIdeaComments_IdeaId_Deleted_Type_CreatedAt",
                table: "ProjectIdeaComments");

            migrationBuilder.DropIndex(
                name: "IX_ActionTaskUpdates_TaskId_IsDeleted_UpdateType_CreatedAtUtc",
                table: "ActionTaskUpdates");

            migrationBuilder.DropColumn(
                name: "CommentType",
                table: "ProjectIdeaComments");

            migrationBuilder.DropColumn(
                name: "CreatedByRole",
                table: "ProjectIdeaComments");

            migrationBuilder.DropColumn(
                name: "StatusSnapshot",
                table: "ProjectIdeaComments");

            migrationBuilder.DropColumn(
                name: "CreatedByRole",
                table: "ActionTaskUpdates");

            migrationBuilder.DropColumn(
                name: "DueDateSnapshot",
                table: "ActionTaskUpdates");

            migrationBuilder.DropColumn(
                name: "StatusSnapshot",
                table: "ActionTaskUpdates");
        }
    }
}
