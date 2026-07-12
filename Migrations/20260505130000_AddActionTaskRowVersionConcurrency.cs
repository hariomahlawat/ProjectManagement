using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Preserves the historical migration identity used by an earlier Action Tracker build.
/// The original bytea row-version implementation was superseded by the later idempotent
/// Action Tracker migration-order repairs and the canonical
/// 20261125170000_EnsureActionTaskRowVersionColumn migration.
///
/// Existing databases may already record this identifier. Fresh or partially upgraded
/// databases are brought to the current schema by the later canonical migration, so this
/// bridge deliberately performs no DDL.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260505130000_AddActionTaskRowVersionConcurrency")]
public sealed class AddActionTaskRowVersionConcurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical lineage bridge only.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Forward-only lineage marker. Reconstructing the obsolete concurrency column is unsafe.
    }
}
