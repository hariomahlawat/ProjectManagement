using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125140000_AddActionTasks")]
    public class AddActionTasks : Migration
    {
        // SECTION: Apply Action Tracker schema
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AssignedToUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedToRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    Priority = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActionTaskAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PerformedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionTaskAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionTaskAuditLogs_ActionTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "ActionTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTaskAuditLogs_PerformedByUserId",
                table: "ActionTaskAuditLogs",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionTaskAuditLogs_TaskId_PerformedAt",
                table: "ActionTaskAuditLogs",
                columns: new[] { "TaskId", "PerformedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTasks_AssignedToUserId_Status",
                table: "ActionTasks",
                columns: new[] { "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTasks_DueDate_Status",
                table: "ActionTasks",
                columns: new[] { "DueDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTasks_IsDeleted",
                table: "ActionTasks",
                column: "IsDeleted");
        }

        // SECTION: Revert Action Tracker schema
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ActionTaskAuditLogs");
            migrationBuilder.DropTable(name: "ActionTasks");
        }
    }
}
