using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanApprovedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanApprovedByUserId",
                table: "Projects",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPlanDurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPlanDurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPlanDurations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectScheduleSettings",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    IncludeWeekends = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SkipHolidays = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NextStageStartPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "NextWorkingDay"),
                    AnchorStart = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectScheduleSettings", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_ProjectScheduleSettings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PlanApprovedByUserId",
                table: "Projects",
                column: "PlanApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date",
                table: "Holidays",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanDurations_ProjectId_StageCode",
                table: "ProjectPlanDurations",
                columns: new[] { "ProjectId", "StageCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AspNetUsers_PlanApprovedByUserId",
                table: "Projects",
                column: "PlanApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(@"
INSERT INTO ""ProjectScheduleSettings"" (""ProjectId"", ""IncludeWeekends"", ""SkipHolidays"", ""NextStageStartPolicy"", ""AnchorStart"")
SELECT ""Id"", FALSE, TRUE, 'NextWorkingDay', NULL
FROM ""Projects"";
");

            migrationBuilder.Sql(@"
WITH ordered AS (
    SELECT
        ""ProjectId"",
        ""StageCode"",
        ROW_NUMBER() OVER (PARTITION BY ""ProjectId"" ORDER BY ""Id"") AS rn
    FROM ""ProjectStages""
    WHERE ""StageCode"" IS NOT NULL
)
INSERT INTO ""ProjectPlanDurations"" (""ProjectId"", ""StageCode"", ""DurationDays"", ""SortOrder"")
SELECT
    o.""ProjectId"",
    o.""StageCode"",
    NULL,
    CASE o.""StageCode""
        WHEN 'FS' THEN 0
        WHEN 'IPA' THEN 1
        WHEN 'SOW' THEN 2
        WHEN 'AON' THEN 3
        WHEN 'BID' THEN 4
        WHEN 'TEC' THEN 5
        WHEN 'BM' THEN 6
        WHEN 'COB' THEN 7
        WHEN 'PNC' THEN 8
        WHEN 'EAS' THEN 9
        WHEN 'SO' THEN 10
        WHEN 'DEVP' THEN 11
        WHEN 'ATP' THEN 12
        WHEN 'PAYMENT' THEN 13
        ELSE 1000 + o.rn
    END
FROM ordered o;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_PlanApprovedByUserId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "ProjectPlanDurations");

            migrationBuilder.DropTable(
                name: "ProjectScheduleSettings");

            migrationBuilder.DropIndex(
                name: "IX_Projects_PlanApprovedByUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PlanApprovedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PlanApprovedByUserId",
                table: "Projects");
        }
    }
}
