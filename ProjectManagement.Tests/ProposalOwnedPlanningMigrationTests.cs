using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProposalOwnedPlanningMigrationTests
{
    public const string PreviousBoundary = "20261217000000_AddCurrentPncApplicability";
    public const string MigrationId = "20261217000001_AddProposalOwnedPlanningInputs";

    public const string CutoverMigrationId = "20261217000002_VersionPlanningInputAuthority";

    [Fact]
    public void CutoverMigration_ClassifiesEveryExistingRowAsLegacyWithoutPromotingOrOverwritingInputs()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_metadata_only;Username=unused;Password=unused").Options);
        Assert.Contains(CutoverMigrationId, db.Database.GetMigrations());
        Assert.Equal(0, db.Model.FindEntityType(typeof(Project))!.FindProperty("PublishedPlanningInputVersion")!.GetDefaultValue());
        Assert.Equal(0, db.Model.FindEntityType(typeof(PlanVersion))!.FindProperty("PlanningInputVersion")!.GetDefaultValue());
        var sql = db.GetService<IMigrator>().GenerateScript(MigrationId, CutoverMigrationId);
        Assert.Contains("ADD \"PublishedPlanningInputVersion\" integer NOT NULL DEFAULT 0", sql);
        Assert.Contains("ADD \"PlanningInputVersion\" integer NOT NULL DEFAULT 0", sql);
        Assert.DoesNotContain("UPDATE ", sql);
        Assert.DoesNotContain("DELETE ", sql);
        var down = db.GetService<IMigrator>().GenerateScript(CutoverMigrationId, MigrationId);
        Assert.Contains("DROP COLUMN \"PlanningInputVersion\"", down);
        Assert.Contains("DROP COLUMN \"PublishedPlanningInputVersion\"", down);
        Assert.DoesNotContain("DROP TABLE", down);
    }

    [Fact]
    public void Migration_AddsOnlyMissingProposalInputsAndHasDeterministicBackfill()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_metadata_only;Username=unused;Password=unused").Options);
        Assert.Contains(MigrationId, db.Database.GetMigrations());
        var property = db.Model.FindEntityType(typeof(PlanVersion))!.FindProperty(nameof(PlanVersion.SkipHolidays))!;
        Assert.Equal(true, property.GetDefaultValue());
        var sql = db.GetService<IMigrator>().GenerateScript(PreviousBoundary, MigrationId);
        Assert.Contains("ADD \"SkipHolidays\" boolean NOT NULL DEFAULT TRUE", sql);
        Assert.Contains("ADD \"SortOrder\" integer NOT NULL DEFAULT 0", sql);
        Assert.Contains("\"ProjectScheduleSettings\"", sql);
        Assert.Contains("\"ProjectPlanDurations\"", sql);
        Assert.Contains("\"ProjectStages\"", sql);
        Assert.Contains("\"StageTemplates\"", sql);
        Assert.DoesNotContain("SET \"AnchorDate\"", sql);
        Assert.DoesNotContain("SET \"DurationDays\"", sql);
        var down = db.GetService<IMigrator>().GenerateScript(MigrationId, PreviousBoundary);
        Assert.Contains("DROP COLUMN \"SkipHolidays\"", down);
        Assert.Contains("DROP COLUMN \"SortOrder\"", down);
        Assert.DoesNotContain("DROP TABLE", down);
    }
}
