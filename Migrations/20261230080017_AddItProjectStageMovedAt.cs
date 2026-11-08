using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// IT projects redesign PR 1 (docs/ux/it-projects-redesign.md): one nullable column recording
    /// when a stage save last changed the current stage. Additive and nullable, so old code ignores
    /// it during an overlapping deployment and existing rows keep null (shown as "date not
    /// recorded"). Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261230080017_AddItProjectStageMovedAt")]
    public partial class AddItProjectStageMovedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StageMovedAtUtc",
                table: "ItProjects",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StageMovedAtUtc",
                table: "ItProjects");
        }
    }
}
