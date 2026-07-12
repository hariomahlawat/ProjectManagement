using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaLibraryMigrationMetadataTests
{
    [Fact]
    public void EveryMediaMigration_IsDiscoverableUniqueAndBoundToMediaContext()
    {
        using var context = CreateMetadataContext();
        var discoveredIds = context.Database.GetMigrations().ToArray();
        Assert.NotEmpty(discoveredIds);

        var migrationTypes = typeof(MediaLibraryDbContext).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract
                && typeof(Migration).IsAssignableFrom(type)
                && string.Equals(
                    type.Namespace,
                    "ProjectManagement.Features.MediaLibrary.Data.Migrations",
                    StringComparison.Ordinal))
            .ToArray();

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
            "Media migration types without immutable EF metadata: " +
            string.Join(", ", missingMetadata));

        var wrongContexts = metadata
            .Where(item => item.DbContext!.ContextType != typeof(MediaLibraryDbContext))
            .Select(item => item.Type.FullName)
            .ToArray();
        Assert.True(
            wrongContexts.Length == 0,
            "Media migrations bound to the wrong DbContext: " +
            string.Join(", ", wrongContexts));

        var attributeIds = metadata.Select(item => item.Migration!.Id).ToArray();
        Assert.Equal(attributeIds.Length, attributeIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(discoveredIds, discoveredIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.True(
            discoveredIds.ToHashSet(StringComparer.Ordinal).SetEquals(attributeIds),
            "Media migration type metadata and EF discovery returned different identifiers.");
        Assert.Equal(
            "__EFMigrationsHistory_MediaLibrary",
            MediaLibraryDbContext.MigrationsHistoryTable);
    }

    [Fact]
    public void MigrationManifest_MatchesTheImmutableMediaLineage()
    {
        using var context = CreateMetadataContext();
        var discovered = context.Database.GetMigrations().ToArray();
        var manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "media-migration-ids.txt");
        Assert.True(File.Exists(manifestPath), $"Migration manifest is missing: {manifestPath}");

        var manifest = File.ReadAllLines(manifestPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        Assert.Equal(manifest, discovered);
    }

    [Fact]
    public void RuntimeSchemaService_DoesNotExposeAMigrationCommand()
    {
        Assert.DoesNotContain(
            typeof(IMediaLibrarySchemaService).GetMethods(),
            method => string.Equals(method.Name, "MigrateAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void SchemaReadiness_DerivesTheLatestMigrationFromTheDeployedAssembly()
    {
        using var context = CreateMetadataContext();
        var migrations = context.Database.GetMigrations().ToArray();

        var resolved = MediaLibrarySchemaService.ResolveLatestRequiredMigrationId(migrations);

        Assert.Equal(migrations[^1], resolved);
        Assert.Equal("20260630113000_AddIdentityReferenceGovernance", resolved);
    }

    private static MediaLibraryDbContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=prism_metadata_only;Username=unused;Password=unused",
                npgsql => npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable))
            .Options;

        // Migration metadata discovery does not open this connection.
        return new MediaLibraryDbContext(options);
    }
}
