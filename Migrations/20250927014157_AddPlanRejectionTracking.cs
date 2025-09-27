using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddPlanRejectionTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "PlanVersions",
                newName: "RejectionNote");

            migrationBuilder.AddColumn<string>(
                name: "RejectedByUserId",
                table: "PlanVersions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedOn",
                table: "PlanVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_RejectedByUserId",
                table: "PlanVersions",
                column: "RejectedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlanVersions_AspNetUsers_RejectedByUserId",
                table: "PlanVersions",
                column: "RejectedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlanVersions_AspNetUsers_RejectedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropIndex(
                name: "IX_PlanVersions_RejectedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "PlanVersions");

            migrationBuilder.DropColumn(
                name: "RejectedOn",
                table: "PlanVersions");

            migrationBuilder.RenameColumn(
                name: "RejectionNote",
                table: "PlanVersions",
                newName: "Reason");
        }
    }
}
