using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class ProjectMetaChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectMetaChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    RequestNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DecisionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecisionNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RequestedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMetaChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMetaChangeRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMetaChangeRequests_ProjectId",
                table: "ProjectMetaChangeRequests",
                column: "ProjectId");

            if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.CreateIndex(
                    name: "ux_projectmetachangerequests_pending",
                    table: "ProjectMetaChangeRequests",
                    column: "ProjectId",
                    unique: true,
                    filter: "[DecisionStatus] = 'Pending'");
            }
            else if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.CreateIndex(
                    name: "ux_projectmetachangerequests_pending",
                    table: "ProjectMetaChangeRequests",
                    column: "ProjectId",
                    unique: true,
                    filter: "\"DecisionStatus\" = 'Pending'");
            }
            else
            {
                migrationBuilder.CreateIndex(
                    name: "ux_projectmetachangerequests_pending",
                    table: "ProjectMetaChangeRequests",
                    column: "ProjectId");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectMetaChangeRequests");
        }
    }
}
