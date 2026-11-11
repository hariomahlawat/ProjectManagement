using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// MOLTI projects and SRE projects, PR 2 (docs/ux/molti-sre-projects.md): the module a project
    /// belongs to and its assigned Project Officer. Additive: every existing row becomes an IT
    /// project through the column default, and the officer columns are nullable, so a process of the
    /// previous build that is still running keeps working during an overlapping deployment. The
    /// previous build does not start against the migrated database (startup refuses a migration
    /// it does not know): going back follows the rollback SOP, a database restore or this
    /// migration's Down. Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270102080020_AddItProjectModuleAndOfficer")]
    public partial class AddItProjectModuleAndOfficer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "ItProjects",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "IT");

            migrationBuilder.AddColumn<string>(
                name: "ProjectOfficerUserId",
                table: "ItProjects",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProjectOfficerAssignedAtUtc",
                table: "ItProjects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItProjects_Module_FinancialYearStart",
                table: "ItProjects",
                columns: new[] { "Module", "FinancialYearStart" });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjects_ProjectOfficerUserId",
                table: "ItProjects",
                column: "ProjectOfficerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ItProjects_ProjectOfficerUserId",
                table: "ItProjects");

            migrationBuilder.DropIndex(
                name: "IX_ItProjects_Module_FinancialYearStart",
                table: "ItProjects");

            migrationBuilder.DropColumn(
                name: "ProjectOfficerAssignedAtUtc",
                table: "ItProjects");

            migrationBuilder.DropColumn(
                name: "ProjectOfficerUserId",
                table: "ItProjects");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "ItProjects");
        }
    }
}
