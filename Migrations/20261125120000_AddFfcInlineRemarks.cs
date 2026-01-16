using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125120000_AddFfcInlineRemarks")]
    public partial class AddFfcInlineRemarks : Migration
    {
        // SECTION: Add inline remark columns
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProgressRemarks",
                table: "FfcProjects",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProgressRemarksUpdatedAtUtc",
                table: "FfcProjects",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgressRemarksUpdatedByUserId",
                table: "FfcProjects",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "FfcProjects",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[16]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverallRemarksUpdatedAtUtc",
                table: "FfcRecords",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverallRemarksUpdatedByUserId",
                table: "FfcRecords",
                maxLength: 450,
                nullable: true);
        }

        // SECTION: Remove inline remark columns
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProgressRemarks",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "ProgressRemarksUpdatedAtUtc",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "ProgressRemarksUpdatedByUserId",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "OverallRemarksUpdatedAtUtc",
                table: "FfcRecords");

            migrationBuilder.DropColumn(
                name: "OverallRemarksUpdatedByUserId",
                table: "FfcRecords");
        }
    }
}
