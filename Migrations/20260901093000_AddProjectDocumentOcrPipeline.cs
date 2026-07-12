using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Preserves the historical OCR-pipeline migration identity used by earlier PRISM builds.
/// Its original non-idempotent PostgreSQL DDL is superseded by the later schema-repair,
/// full-text-search restoration and OCR-pipeline reconciliation migrations.
///
/// Replaying the obsolete migration after those later identifiers have already been
/// recorded could overwrite current trigger definitions. The bridge is therefore a no-op
/// while the later canonical migrations remain responsible for the final physical schema.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260901093000_AddProjectDocumentOcrPipeline")]
public sealed class AddProjectDocumentOcrPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical lineage bridge only. Later idempotent migrations establish the schema.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Forward-only lineage marker. Do not restore obsolete OCR/search definitions.
    }
}
