using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTotTrackerWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastApprovedByUserId",
                table: "ProjectTots",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastApprovedOnUtc",
                table: "ProjectTots",
                type: "timestamp without time zone",
                nullable: true);

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
                    SubmittedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DecisionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DecisionRemarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
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
                name: "IX_ProjectTots_LastApprovedByUserId",
                table: "ProjectTots",
                column: "LastApprovedByUserId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectTots_AspNetUsers_LastApprovedByUserId",
                table: "ProjectTots",
                column: "LastApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectTots_AspNetUsers_LastApprovedByUserId",
                table: "ProjectTots");

            migrationBuilder.DropTable(
                name: "ProjectTotRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProjectTots_LastApprovedByUserId",
                table: "ProjectTots");

            migrationBuilder.DropColumn(
                name: "LastApprovedByUserId",
                table: "ProjectTots");

            migrationBuilder.DropColumn(
                name: "LastApprovedOnUtc",
                table: "ProjectTots");
        }
    }
}
