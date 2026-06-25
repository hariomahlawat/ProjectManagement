using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261126110000_AddNotebookCollaboration")]
public partial class AddNotebookCollaboration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NotebookItemCollaborators",
            columns: table => new
            {
                NotebookItemId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Role = table.Column<byte>(type: "smallint", nullable: false),
                AddedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                AddedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotebookItemCollaborators", x => new { x.NotebookItemId, x.UserId });
                table.ForeignKey(
                    name: "FK_NotebookItemCollaborators_AspNetUsers_AddedByUserId",
                    column: x => x.AddedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_NotebookItemCollaborators_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_NotebookItemCollaborators_NotebookItems_NotebookItemId",
                    column: x => x.NotebookItemId,
                    principalTable: "NotebookItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NotebookItemCollaborators_AddedByUserId",
            table: "NotebookItemCollaborators",
            column: "AddedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_NotebookItemCollaborators_UserId",
            table: "NotebookItemCollaborators",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_NotebookItemCollaborators_UserId_Role",
            table: "NotebookItemCollaborators",
            columns: new[] { "UserId", "Role" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NotebookItemCollaborators");
    }
}
