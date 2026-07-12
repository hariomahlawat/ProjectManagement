using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Restores an immutable historical migration identifier recorded by deployed PRISM
/// databases. The original migration repaired null values in legacy records consumed by
/// the compendium. Current schema constraints and application writes already prevent those
/// legacy nulls, so a fresh database requires no additional operation.
///
/// Keep this migration alongside 20261201090000_AlignProjectStageBackfillConstraint. EF
/// Core migration identity is the complete identifier, not only its timestamp prefix.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201090000_FixLegacyNullsForCompendiums")]
public sealed class FixLegacyNullsForCompendiums : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical lineage bridge. The production databases that contain this history
        // entry have already received the original legacy-data repair.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty. Reintroducing legacy nulls would be unsafe and destructive.
    }
}
