using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations;

public partial class ProjectTotProgressUpdates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectTotProgressUpdates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                SubmittedByRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SubmittedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                DecidedByRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                DecidedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                DecisionRemarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                PublishedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false, defaultValue: new byte[0])
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectTotProgressUpdates", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectTotProgressUpdates_AspNetUsers_DecidedByUserId",
                    column: x => x.DecidedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProjectTotProgressUpdates_AspNetUsers_SubmittedByUserId",
                    column: x => x.SubmittedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProjectTotProgressUpdates_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTotProgressUpdates_DecidedByUserId",
            table: "ProjectTotProgressUpdates",
            column: "DecidedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTotProgressUpdates_ProjectId",
            table: "ProjectTotProgressUpdates",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTotProgressUpdates_ProjectId_State",
            table: "ProjectTotProgressUpdates",
            columns: new[] { "ProjectId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTotProgressUpdates_SubmittedByUserId",
            table: "ProjectTotProgressUpdates",
            column: "SubmittedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProjectTotProgressUpdates");
    }
}
