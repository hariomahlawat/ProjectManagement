using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125190000_AddActionSprintAuditAndConcurrency")]
    public partial class AddActionSprintAuditAndConcurrency : Migration
    {
        // SECTION: Add sprint lifecycle audit storage and concurrency token
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ActionSprints",
                type: "bytea",
                nullable: false,
                defaultValue: Array.Empty<byte>());

            migrationBuilder.CreateTable(
                name: "ActionSprintAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SprintId = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PerformedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionSprintAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionSprintAuditLogs_ActionSprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "ActionSprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionSprintAuditLogs_PerformedByUserId",
                table: "ActionSprintAuditLogs",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionSprintAuditLogs_SprintId_PerformedAt",
                table: "ActionSprintAuditLogs",
                columns: new[] { "SprintId", "PerformedAt" });
        }

        // SECTION: Remove sprint lifecycle audit storage and concurrency token
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionSprintAuditLogs");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ActionSprints");
        }
    }
}
