using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// End-to-end external-folder scan on PostgreSQL. The final step that hides files not seen
/// by this scan is guarded on still holding the scan lease, in the same statement.
/// </summary>
public sealed class ExternalScanPostgresTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"scan-pg-{Guid.NewGuid():N}");

    public ExternalScanPostgresTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.jpg"), "a");
        File.WriteAllText(Path.Combine(_root, "b.jpg"), "b");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [PostgresMigrationFact]
    public async Task Scan_indexes_present_files_and_hides_a_catalogued_file_that_is_gone()
    {
        await WithDatabaseAsync(async target =>
        {
            var sourceId = await SeedAsync(target);

            await using (var db = Create(target))
            {
                await CreateScanner(db).ScanAsync(sourceId, "worker-a", CancellationToken.None);
            }

            await using var check = Create(target);
            var assets = await check.Assets.AsNoTracking().ToDictionaryAsync(asset => asset.RelativePath!);
            Assert.True(assets["a.jpg"].IsAvailable);
            Assert.True(assets["b.jpg"].IsAvailable);
            Assert.False(assets["gone.jpg"].IsAvailable);
            var source = await check.Sources.AsNoTracking().SingleAsync();
            Assert.Equal("Healthy", source.ScanStatus);
        });
    }

    private async Task<Guid> SeedAsync(string target)
    {
        await using var db = Create(target);
        await db.Database.MigrateAsync();
        var source = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = "nas",
            Name = "NAS",
            SourceType = MediaLibrarySourceType.FileSystem,
            RootPath = _root,
            IsEnabled = true,
            IsVisibleInLibrary = true,
            AllowedExtensionsJson = "[\".jpg\"]",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Sources.Add(source);
        db.Assets.Add(new MediaAsset
        {
            SourceId = source.Id,
            Origin = MediaAssetOrigin.ExternalFile,
            Kind = MediaAssetKind.Photo,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = "gone.jpg",
            RelativePath = "gone.jpg",
            ContextKey = "external:nas:root",
            CollectionKey = "external:nas:root",
            ContextTitle = "NAS",
            ContextSubtitle = "NAS",
            SourceLabel = "NAS",
            Title = "gone",
            OriginalFileName = "gone.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = DateTimeOffset.UtcNow,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        return source.Id;
    }

    private static FileSystemMediaSourceScanner CreateScanner(MediaLibraryDbContext db)
    {
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            ExternalSources = new ExternalMediaSourcesOptions { Enabled = true, ScanBatchSize = 1 },
            Processing = new MediaProcessingOptions { WorkerEnabled = false }
        };
        return new FileSystemMediaSourceScanner(
            db,
            Options.Create(options),
            new FileSystemPathResolver(),
            new SafeFileEnumerator(NullLogger<SafeFileEnumerator>.Instance),
            new FixedMetadataReader(),
            new MediaContentChangeInvalidationService(db, new NoCache(), NullLogger<MediaContentChangeInvalidationService>.Instance),
            NullLogger<FileSystemMediaSourceScanner>.Instance);
    }

    private static async Task WithDatabaseAsync(Func<string, Task> body)
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_scan_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;
        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await body(target);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static MediaLibraryDbContext Create(string connectionString)
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable))
            .Options);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoCache : IMediaCachePathResolver
    {
        public string CacheRoot => Path.GetTempPath();
        public string GetThumbnailPath(long assetId, int cacheVersion) => throw new NotSupportedException();
        public string GetPreviewPath(long assetId, int cacheVersion) => throw new NotSupportedException();
    }

    private sealed class FixedMetadataReader : IMediaMetadataReader
    {
        public Task<MediaFileMetadata> ReadAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(new MediaFileMetadata(
                MediaAssetKind.Photo, "image/jpeg", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                10, 10, null, false, null, null));

        public Task<MediaFileMetadata> ReadAsync(MediaContentDescriptor content, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
