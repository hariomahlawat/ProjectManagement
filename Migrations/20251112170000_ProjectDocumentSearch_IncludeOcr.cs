using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Preserves a historical migration identifier that existed in earlier PRISM builds.
/// The original PostgreSQL search-vector implementation is superseded by the later
/// idempotent project-document search migrations, which establish the current schema
/// and trigger definitions for both upgraded and fresh databases.
///
/// This migration must remain discoverable because deployed databases may already
/// contain its identifier in __EFMigrationsHistory. Replaying the obsolete trigger
/// definitions on a database where later migrations are already recorded would regress
/// the live search pipeline, so the lineage bridge is intentionally operation-free.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20251112170000_ProjectDocumentSearch_IncludeOcr")]
public sealed class ProjectDocumentSearch_IncludeOcr : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical lineage bridge only. Current project-document OCR/search schema is
        // established by the later PostgreSQL repair and reconciliation migrations.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Forward-only lineage marker. Restoring obsolete trigger definitions is unsafe.
    }
}
