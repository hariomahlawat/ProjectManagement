using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Restores the immutable historical migration identifier already recorded in an
/// existing PRISM database. The original migration adjusted the Projects.WorkflowVersion
/// column length. The current migration chain later applies
/// 20261125140000_FixProjectWorkflowVersionColumnLength, which establishes the current
/// 64-character schema, so replaying unknown historical DDL is unnecessary and unsafe.
///
/// Do not rename or remove this migration after deployment. EF Core migration identifiers
/// form a permanent compatibility contract with __EFMigrationsHistory.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261125123000_AdjustProjectWorkflowVersionLength")]
public sealed class AdjustProjectWorkflowVersionLength : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical lineage bridge only. Existing databases containing this history
        // entry have already executed the original migration. Fresh databases are brought
        // to the current 64-character WorkflowVersion definition by the later canonical
        // 20261125140000_FixProjectWorkflowVersionColumnLength migration.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty. Reconstructing an unknown earlier column-length state would
        // be unsafe and is not required for supported forward-only deployments.
    }
}
