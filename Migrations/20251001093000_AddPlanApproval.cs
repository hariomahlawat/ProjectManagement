using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "PlanVersions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedOn",
                table: "PlanVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "PlanVersions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedByUserId",
                table: "PlanVersions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedOn",
                table: "PlanVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanVersionId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PerformedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanApprovalLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanApprovalLogs_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanApprovalLogs_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_ApprovedByUserId",
                table: "PlanVersions",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_SubmittedByUserId",
                table: "PlanVersions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanApprovalLogs_PerformedByUserId",
                table: "PlanApprovalLogs",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanApprovalLogs_PlanVersionId",
                table: "PlanApprovalLogs",
                column: "PlanVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlanVersions_AspNetUsers_ApprovedByUserId",
                table: "PlanVersions",
                column: "ApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PlanVersions_AspNetUsers_SubmittedByUserId",
                table: "PlanVersions",
                column: "SubmittedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlanVersions_AspNetUsers_ApprovedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_PlanVersions_AspNetUsers_SubmittedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropTable(
                name: "PlanApprovalLogs");

            migrationBuilder.DropIndex(
                name: "IX_PlanVersions_ApprovedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropIndex(
                name: "IX_PlanVersions_SubmittedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedOn",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "SubmittedOn",
                table: "PlanVersions");
        }
    }
}
