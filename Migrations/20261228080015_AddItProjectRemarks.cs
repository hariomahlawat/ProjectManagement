using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// IT project remarks (docs/ux/it-projects.md, PR 4): a remark table and its immutable audit
    /// snapshots, keyed to ItProjects only. Additive: two new tables nothing else depends on, so
    /// safe during an overlapping deployment and unused after a rollback. Hand-written because the
    /// model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261228080015_AddItProjectRemarks")]
    public partial class AddItProjectRemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItProjectRemarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItProjectId = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastEditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItProjectRemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItProjectRemarks_ItProjects_ItProjectId",
                        column: x => x.ItProjectId,
                        principalTable: "ItProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItProjectRemarkAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RemarkId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ActionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotBody = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SnapshotEventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SnapshotStageCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SnapshotIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItProjectRemarkAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItProjectRemarkAudits_ItProjectRemarks_RemarkId",
                        column: x => x.RemarkId,
                        principalTable: "ItProjectRemarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjectRemarks_Project_Deleted_EventDate_Created",
                table: "ItProjectRemarks",
                columns: new[] { "ItProjectId", "IsDeleted", "EventDate", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjectRemarkAudits_RemarkId_ActionAtUtc",
                table: "ItProjectRemarkAudits",
                columns: new[] { "RemarkId", "ActionAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItProjectRemarkAudits");

            migrationBuilder.DropTable(
                name: "ItProjectRemarks");
        }
    }
}
