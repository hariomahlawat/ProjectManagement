using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddRemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Remarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StageRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StageNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastEditedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DeletedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Remarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Remarks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemarkAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RemarkId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnapshotType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnapshotAuthorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotAuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SnapshotEventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SnapshotStageRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnapshotStageName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SnapshotBody = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SnapshotLastEditedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SnapshotIsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    SnapshotDeletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SnapshotDeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SnapshotDeletedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnapshotProjectId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ActorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Meta = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemarkAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemarkAudits_Remarks_RemarkId",
                        column: x => x.RemarkId,
                        principalTable: "Remarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemarkAudits_RemarkId",
                table: "RemarkAudits",
                column: "RemarkId");

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ProjectId_IsDeleted_CreatedAtUtc",
                table: "Remarks",
                columns: new[] { "ProjectId", "IsDeleted", "CreatedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ProjectId_IsDeleted_Type_EventDate",
                table: "Remarks",
                columns: new[] { "ProjectId", "IsDeleted", "Type", "EventDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemarkAudits");

            migrationBuilder.DropTable(
                name: "Remarks");
        }
    }
}
