using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Keeps the summaries of "Copy PDFs into the repository" runs in the database instead of in
    /// each process's memory, so every server shows the same history and it survives a restart
    /// (docs/ux/admin-review-3-fixes.md, C8, PR 9).
    /// Additive: a new table nothing else depends on. A server still running the previous release
    /// ignores it, so it is safe during an overlapping deployment, and rolling the application back
    /// leaves it unused. Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261226080013_AddPdfIngestionRuns")]
    public partial class AddPdfIngestionRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PdfIngestionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitiatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Discovered = table.Column<int>(type: "integer", nullable: false),
                    IngestedOrLinked = table.Column<int>(type: "integer", nullable: false),
                    AlreadyLinked = table.Column<int>(type: "integer", nullable: false),
                    Missing = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    SourcesJson = table.Column<string>(type: "text", nullable: false),
                    FailuresJson = table.Column<string>(type: "text", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfIngestionRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PdfIngestionRuns_CompletedAtUtc",
                table: "PdfIngestionRuns",
                column: "CompletedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PdfIngestionRuns");
        }
    }
}
