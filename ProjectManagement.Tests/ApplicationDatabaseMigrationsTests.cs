using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ApplicationDatabaseMigrationsTests
{
    [Fact]
    public void MigrationAssembly_PreservesHistoricalProductionLineage()
    {
        using var context = CreateMetadataContext();
        var migrations = context.Database.GetMigrations().ToArray();

        Assert.Contains("20251112170000_ProjectDocumentSearch_IncludeOcr", migrations);
        Assert.Contains("20260505130000_AddActionTaskRowVersionConcurrency", migrations);
        Assert.Contains("20260901093000_AddProjectDocumentOcrPipeline", migrations);
        Assert.Contains("20261125123000_AdjustProjectWorkflowVersionDefaults", migrations);
        Assert.Contains("20261125123000_AdjustProjectWorkflowVersionLength", migrations);
        Assert.Contains("20261201090000_FixLegacyNullsForCompendiums", migrations);
        Assert.Contains("20261201090000_AlignProjectStageBackfillConstraint", migrations);
        Assert.Contains("20261201150000_ReconcileProjectStageCompletionConstraint", migrations);
        Assert.Contains("20261201160000_FinalizeProjectStageCompletionConstraint", migrations);
        Assert.Contains("20261201170000_AddConferenceRemarkFoundation", migrations);
        Assert.Contains("20261201180000_AdminPhaseASafetyHardening", migrations);
        Assert.Contains("20261201190000_AdminPhaseB3MasterDataHardening", migrations);
        Assert.Contains("20261201260000_HardenFfcFoundation", migrations);
        Assert.Contains("20261201270000_AddFfcProjectConcurrency", migrations);
        Assert.Equal(migrations.Length, migrations.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryApplicationMigrationType_IsDiscoverableAndHasImmutableMetadata()
    {
        using var context = CreateMetadataContext();
        var discoveredIds = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);

        var migrationTypes = typeof(ApplicationDbContext).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract
                && typeof(Migration).IsAssignableFrom(type)
                && string.Equals(type.Namespace, "ProjectManagement.Migrations", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(migrationTypes);

        var metadata = migrationTypes.Select(type => new
        {
            Type = type,
            Migration = type.GetCustomAttribute<MigrationAttribute>(),
            DbContext = type.GetCustomAttribute<DbContextAttribute>()
        }).ToArray();

        var missingMetadata = metadata
            .Where(item => item.Migration is null || item.DbContext is null)
            .Select(item => item.Type.FullName)
            .ToArray();
        Assert.True(
            missingMetadata.Length == 0,
            "Migration types without [Migration] and [DbContext] metadata: " +
            string.Join(", ", missingMetadata));

        var wrongContexts = metadata
            .Where(item => item.DbContext!.ContextType != typeof(ApplicationDbContext))
            .Select(item => item.Type.FullName)
            .ToArray();
        Assert.True(
            wrongContexts.Length == 0,
            "Application migrations bound to the wrong DbContext: " +
            string.Join(", ", wrongContexts));

        var attributeIds = metadata
            .Select(item => item.Migration!.Id)
            .ToArray();
        var duplicateIds = attributeIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(
            duplicateIds.Length == 0,
            "Duplicate ApplicationDbContext migration identifiers: " +
            string.Join(", ", duplicateIds));

        var missingFromEf = attributeIds
            .Where(id => !discoveredIds.Contains(id))
            .ToArray();
        Assert.True(
            missingFromEf.Length == 0,
            "Attributed migrations not discoverable through EF Core: " +
            string.Join(", ", missingFromEf));

        var missingTypes = discoveredIds
            .Where(id => !attributeIds.Contains(id, StringComparer.Ordinal))
            .ToArray();
        Assert.True(
            missingTypes.Length == 0,
            "EF Core migration identifiers without a corresponding migration type: " +
            string.Join(", ", missingTypes));
    }

    [Fact]
    public void MigrationManifest_MatchesTheImmutableApplicationLineage()
    {
        using var context = CreateMetadataContext();
        var discovered = context.Database.GetMigrations().ToArray();
        var manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "application-migration-ids.txt");
        Assert.True(File.Exists(manifestPath), $"Migration manifest is missing: {manifestPath}");

        var manifest = File.ReadAllLines(manifestPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        Assert.Equal(manifest, discovered);
    }

    [Fact]
    public void MigrationIdentifiers_AreOrderedAndFinalRepairRemainsTheTail()
    {
        using var context = CreateMetadataContext();
        var migrations = context.Database.GetMigrations().ToArray();

        Assert.Equal(
            migrations.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            migrations);
        Assert.Equal(
            "20261201270000_AddFfcProjectConcurrency",
            migrations[^1]);
    }

    private static ApplicationDbContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_metadata_only;Username=unused;Password=unused")
            .Options;

        // GetMigrations inspects assembly metadata only and does not open this connection.
        return new ApplicationDbContext(options);
    }
}
