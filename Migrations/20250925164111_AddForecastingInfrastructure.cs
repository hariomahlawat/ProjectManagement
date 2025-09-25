using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastingInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "StagePlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ForecastDue",
                table: "ProjectStages",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ForecastStart",
                table: "ProjectStages",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StageShiftLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OldForecastDue = table.Column<DateOnly>(type: "date", nullable: true),
                    NewForecastDue = table.Column<DateOnly>(type: "date", nullable: false),
                    DeltaDays = table.Column<int>(type: "integer", nullable: false),
                    CauseStageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CauseType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageShiftLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageShiftLogs_ProjectId_StageCode_CreatedOn",
                table: "StageShiftLogs",
                columns: new[] { "ProjectId", "StageCode", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageShiftLogs");

            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "StagePlans");

            migrationBuilder.DropColumn(
                name: "ForecastDue",
                table: "ProjectStages");

            migrationBuilder.DropColumn(
                name: "ForecastStart",
                table: "ProjectStages");
        }
    }
}
