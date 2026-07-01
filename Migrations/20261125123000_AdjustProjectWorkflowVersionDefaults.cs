using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Restores an immutable historical migration identifier that is already present in
/// deployed PRISM databases. The original migration normalised legacy workflow-version
/// defaults. Its effects are superseded by the current model and later workflow-version
/// migrations, so no additional operation is required for a new database.
///
/// This class must never be renamed or removed: EF Core migration history is a permanent
/// deployment contract once a migration has been applied to a shared database.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261125123000_AdjustProjectWorkflowVersionDefaults")]
public sealed class AdjustProjectWorkflowVersionDefaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical lineage bridge. Existing databases have already executed the
        // original migration; fresh databases contain no legacy project rows to repair.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty. Removing a historical data-normalisation marker would not
        // safely reconstruct the pre-migration legacy state.
    }
}
