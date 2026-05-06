using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125180000_AddActionSprints")]
    public partial class AddActionSprints : Migration
    {
        // SECTION: Add sprint domain tables and task backlog link
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionSprints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Goal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionSprints", x => x.Id);
                });

            migrationBuilder.AddColumn<int>(
                name: "SprintId",
                table: "ActionTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionSprints_IsDeleted",
                table: "ActionSprints",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ActionSprints_StartDate_EndDate",
                table: "ActionSprints",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionSprints_Status",
                table: "ActionSprints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ActionTasks_SprintId",
                table: "ActionTasks",
                column: "SprintId");

            migrationBuilder.AddForeignKey(
                name: "FK_ActionTasks_ActionSprints_SprintId",
                table: "ActionTasks",
                column: "SprintId",
                principalTable: "ActionSprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        // SECTION: Remove sprint domain tables and task backlog link
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionTasks_ActionSprints_SprintId",
                table: "ActionTasks");

            migrationBuilder.DropIndex(
                name: "IX_ActionTasks_SprintId",
                table: "ActionTasks");

            migrationBuilder.DropColumn(
                name: "SprintId",
                table: "ActionTasks");

            migrationBuilder.DropTable(
                name: "ActionSprints");
        }
    }
}
