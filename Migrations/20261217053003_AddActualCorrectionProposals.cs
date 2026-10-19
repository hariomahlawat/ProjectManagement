using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Phase D-2: additive tables for Actual-correction proposals (plan section D.2).
    /// Unused until the D-3 proposal services. Contains only these two tables, their
    /// indexes and constraints; pre-existing model/snapshot drift elsewhere is deliberately
    /// not included.
    /// </summary>
    public partial class AddActualCorrectionProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActualCorrectionRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    RevisionNo = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EntryPoint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SubmittedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupersedesRevisionId = table.Column<int>(type: "integer", nullable: true),
                    SupersededByRevisionId = table.Column<int>(type: "integer", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ClosedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualCorrectionRevisions", x => x.Id);
                    table.CheckConstraint("CK_ActualCorrectionRevisions_Closure", "(\"Status\" = 'Pending' AND \"ClosedOn\" IS NULL AND \"ClosedByUserId\" IS NULL) OR (\"Status\" <> 'Pending' AND \"ClosedOn\" IS NOT NULL AND \"ClosedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_ActualCorrectionRevisions_EntryPoint", "\"EntryPoint\" IN ('Editor','Backfill')");
                    table.CheckConstraint("CK_ActualCorrectionRevisions_RevisionNo", "\"RevisionNo\" > 0");
                    table.CheckConstraint("CK_ActualCorrectionRevisions_Status", "\"Status\" IN ('Pending','Superseded','Withdrawn','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_ActualCorrectionRevisions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActualCorrectionRevisions_SupersededByRevision",
                        column: x => x.SupersededByRevisionId,
                        principalTable: "ActualCorrectionRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualCorrectionRevisions_SupersedesRevision",
                        column: x => x.SupersedesRevisionId,
                        principalTable: "ActualCorrectionRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActualCorrectionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RevisionId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BeforeStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BeforeActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                    BeforeCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    BeforeRequiresBackfill = table.Column<bool>(type: "boolean", nullable: false),
                    ProposedActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ProposedCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ClearsActualStart = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualCorrectionItems", x => x.Id);
                    table.CheckConstraint("CK_ActualCorrectionItems_ClearsActualStart", "NOT \"ClearsActualStart\" OR \"ProposedActualStart\" IS NULL");
                    table.CheckConstraint("CK_ActualCorrectionItems_Kind", "\"Kind\" IN ('Correction','Backfill')");
                    table.ForeignKey(
                        name: "FK_ActualCorrectionItems_ActualCorrectionRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "ActualCorrectionRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_actualcorrectionitems_revision_stage",
                table: "ActualCorrectionItems",
                columns: new[] { "RevisionId", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualCorrectionRevisions_SupersededByRevisionId",
                table: "ActualCorrectionRevisions",
                column: "SupersededByRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualCorrectionRevisions_SupersedesRevisionId",
                table: "ActualCorrectionRevisions",
                column: "SupersedesRevisionId");

            migrationBuilder.CreateIndex(
                name: "ux_actualcorrectionrevisions_pending",
                table: "ActualCorrectionRevisions",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_actualcorrectionrevisions_project_revision",
                table: "ActualCorrectionRevisions",
                columns: new[] { "ProjectId", "RevisionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActualCorrectionItems");

            migrationBuilder.DropTable(
                name: "ActualCorrectionRevisions");
        }
    }
}
