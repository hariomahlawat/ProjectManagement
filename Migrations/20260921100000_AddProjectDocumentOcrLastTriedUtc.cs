using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260921100000_AddProjectDocumentOcrLastTriedUtc")]
    public partial class AddProjectDocumentOcrLastTriedUtc : Migration
    {
        // SECTION: Apply schema changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OcrLastTriedUtc",
                table: "ProjectDocuments",
                type: "timestamp with time zone",
                nullable: true);
        }

        // SECTION: Revert schema changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OcrLastTriedUtc",
                table: "ProjectDocuments");
        }
    }
}
