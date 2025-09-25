using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanVersionAnchorOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AnchorDate",
                table: "PlanVersions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnchorStageCode",
                table: "PlanVersions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PncApplicable",
                table: "PlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SkipWeekends",
                table: "PlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TransitionRule",
                table: "PlanVersions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NextWorkingDay");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnchorDate",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "AnchorStageCode",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "PncApplicable",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "SkipWeekends",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "TransitionRule",
                table: "PlanVersions");
        }
    }
}
