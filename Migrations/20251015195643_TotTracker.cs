using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations;

public partial class TotTracker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectTots",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                LastApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                LastApprovedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectTots", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectTots_AspNetUsers_LastApprovedByUserId",
                    column: x => x.LastApprovedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProjectTots_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProjectTotRequests",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                ProposedStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ProposedStartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                ProposedCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                ProposedRemarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                SubmittedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DecisionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                DecidedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DecisionRemarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false, defaultValue: new byte[0])
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectTotRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectTotRequests_AspNetUsers_DecidedByUserId",
                    column: x => x.DecidedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProjectTotRequests_AspNetUsers_SubmittedByUserId",
                    column: x => x.SubmittedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProjectTotRequests_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTotRequests_DecidedByUserId",
            table: "ProjectTotRequests",
            column: "DecidedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTotRequests_ProjectId",
            table: "ProjectTotRequests",
            column: "ProjectId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTotRequests_SubmittedByUserId",
            table: "ProjectTotRequests",
            column: "SubmittedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTots_LastApprovedByUserId",
            table: "ProjectTots",
            column: "LastApprovedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTots_ProjectId",
            table: "ProjectTots",
            column: "ProjectId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProjectTotRequests");

        migrationBuilder.DropTable(
            name: "ProjectTots");
    }
}
