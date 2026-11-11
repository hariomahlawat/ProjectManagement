using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// A storage outage makes every file lookup report "missing". These tests pin that an
/// unreachable storage root is treated as temporary and never hides catalogue assets.
/// </summary>
public sealed class MediaStorageOutageTests : IDisposable
{
    private readonly string _existingRoot;
    private readonly string _missingRoot;

    public MediaStorageOutageTests()
    {
        _existingRoot = Path.Combine(Path.GetTempPath(), $"media-outage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_existingRoot);
        _missingRoot = Path.Combine(Path.GetTempPath(), $"media-outage-missing-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_existingRoot))
        {
            Directory.Delete(_existingRoot, recursive: true);
        }
    }

    [Fact]
    public void Probe_reports_external_root_reachability()
    {
        var probe = CreateProbe(_existingRoot);

        Assert.True(probe.IsStorageRootReachable(ExternalAsset(_existingRoot)));
        Assert.False(probe.IsStorageRootReachable(ExternalAsset(_missingRoot)));
    }

    [Fact]
    public void Probe_uses_upload_root_for_prism_media()
    {
        var asset = new MediaAsset { Id = 7, Origin = MediaAssetOrigin.ProjectPhoto };

        Assert.True(CreateProbe(_existingRoot).IsStorageRootReachable(asset));
        Assert.False(CreateProbe(_missingRoot).IsStorageRootReachable(asset));
    }

    [Fact]
    public void Probe_preserves_existing_handling_when_root_is_unknown()
    {
        var asset = new MediaAsset
        {
            Origin = MediaAssetOrigin.ExternalFile,
            Source = new MediaLibrarySource { RootPath = null }
        };

        Assert.True(CreateProbe(_existingRoot).IsStorageRootReachable(asset));
    }

    [Fact]
    public async Task Reconciliation_leaves_assets_visible_while_storage_root_is_unreachable()
    {
        await using var db = CreateContext();
        await SeedHistoricalAssetAsync(db, _missingRoot);
        var service = CreateRecoveryService(db);

        var result = await service.ReconcileHistoricalAsync(50, CancellationToken.None);

        var asset = await db.Assets.SingleAsync();
        Assert.True(asset.IsAvailable);
        Assert.Equal(MediaAvailabilityStatus.Available, asset.AvailabilityStatus);
        Assert.Equal(0, result.MarkedUnavailable);
        Assert.Equal(1, result.TemporarilyUnavailable);
        // The pass stops rather than re-probing the same rows every few seconds.
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task Reconciliation_still_marks_a_file_missing_when_its_root_is_reachable()
    {
        await using var db = CreateContext();
        await SeedHistoricalAssetAsync(db, _existingRoot);
        var service = CreateRecoveryService(db);

        var result = await service.ReconcileHistoricalAsync(50, CancellationToken.None);

        var asset = await db.Assets.SingleAsync();
        Assert.False(asset.IsAvailable);
        Assert.Equal(MediaAvailabilityStatus.SourceMissing, asset.AvailabilityStatus);
        Assert.Equal(1, result.MarkedUnavailable);
    }

    [Fact]
    public async Task Restored_asset_stops_being_a_reconciliation_candidate()
    {
        await using var db = CreateContext();
        await SeedHistoricalAssetAsync(db, _existingRoot);
        File.WriteAllText(Path.Combine(_existingRoot, "photo.jpg"), "jpeg");
        // A dead-lettered job of another type used to keep the asset a candidate for ever.
        db.ProcessingJobs.Add(new MediaProcessingJob
        {
            MediaAssetId = 1,
            JobType = MediaProcessingJobType.ClassifyMedia,
            Status = MediaProcessingJobStatus.DeadLetter,
            FailureCode = nameof(FileNotFoundException),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateRecoveryService(db);

        var first = await service.ReconcileHistoricalAsync(50, CancellationToken.None);
        var second = await service.ReconcileHistoricalAsync(50, CancellationToken.None);

        Assert.Equal(1, first.Restored);
        Assert.False(first.HasMore);
        Assert.Equal(0, second.Examined);
        Assert.DoesNotContain(await db.ProcessingJobs.AsNoTracking().ToListAsync(),
            job => job.Status == MediaProcessingJobStatus.DeadLetter);
    }

    [Fact]
    public async Task Reconciliation_does_not_take_a_running_jobs_lease()
    {
        await using var db = CreateContext();
        await SeedHistoricalAssetAsync(db, _existingRoot);
        File.WriteAllText(Path.Combine(_existingRoot, "photo.jpg"), "jpeg");
        var analyse = await db.ProcessingJobs.SingleAsync();
        analyse.JobType = MediaProcessingJobType.ClassifyMedia;
        db.ProcessingJobs.Add(new MediaProcessingJob
        {
            MediaAssetId = 1,
            JobType = MediaProcessingJobType.AnalyseAsset,
            Status = MediaProcessingJobStatus.Running,
            LockedBy = "worker-1",
            LockExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await CreateRecoveryService(db).ReconcileHistoricalAsync(50, CancellationToken.None);

        var running = await db.ProcessingJobs.AsNoTracking()
            .SingleAsync(job => job.JobType == MediaProcessingJobType.AnalyseAsset);
        Assert.Equal(MediaProcessingJobStatus.Running, running.Status);
        Assert.Equal("worker-1", running.LockedBy);
    }

    [Fact]
    public void Storage_unreachable_failures_are_recoverable()
    {
        var failure = new MediaSourceStorageUnreachableException("share offline");

        Assert.False(MediaProcessingFailurePolicy.IsPermanent(failure));
        Assert.False(MediaProcessingFailurePolicy.IsSourceUnavailable(failure));
        Assert.Contains(
            nameof(MediaSourceStorageUnreachableException),
            MediaProcessingFailurePolicy.RecoverableFailureCodeNames);
    }

    private static MediaStorageRootProbe CreateProbe(string uploadRoot)
        => new(
            new FileSystemPathResolver(),
            new TestUploadRootProvider(uploadRoot),
            NullLogger<MediaStorageRootProbe>.Instance);

    private MediaAvailabilityRecoveryService CreateRecoveryService(MediaLibraryDbContext db)
    {
        var paths = new FileSystemPathResolver();
        var resolver = new MediaContentProviderResolver(new IMediaContentProvider[]
        {
            new FileSystemMediaContentProvider(paths)
        });
        return new MediaAvailabilityRecoveryService(
            db,
            resolver,
            CreateProbe(_existingRoot),
            NullLogger<MediaAvailabilityRecoveryService>.Instance);
    }

    private static MediaAsset ExternalAsset(string root)
        => new()
        {
            Id = 1,
            Origin = MediaAssetOrigin.ExternalFile,
            Source = new MediaLibrarySource { RootPath = root }
        };

    private static async Task SeedHistoricalAssetAsync(MediaLibraryDbContext db, string root)
    {
        var source = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = "nas",
            Name = "NAS",
            SourceType = MediaLibrarySourceType.FileSystem,
            RootPath = root,
            IsEnabled = true,
            IsVisibleInLibrary = true
        };
        db.Sources.Add(source);
        var asset = new MediaAsset
        {
            Id = 1,
            SourceId = source.Id,
            Source = source,
            Origin = MediaAssetOrigin.ExternalFile,
            Kind = MediaAssetKind.Photo,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = "photo.jpg",
            RelativePath = "photo.jpg",
            ContextKey = "external:nas:root",
            CollectionKey = "external:nas:root",
            ContextTitle = "NAS",
            ContextSubtitle = "NAS",
            SourceLabel = "NAS",
            Title = "photo",
            OriginalFileName = "photo.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = DateTimeOffset.UtcNow,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow
        };
        db.Assets.Add(asset);
        db.ProcessingJobs.Add(new MediaProcessingJob
        {
            MediaAssetId = asset.Id,
            JobType = MediaProcessingJobType.AnalyseAsset,
            Status = MediaProcessingJobStatus.DeadLetter,
            FailureCode = nameof(FileNotFoundException),
            FailureMessage = "Could not find file.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static MediaLibraryDbContext CreateContext()
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-outage-{Guid.NewGuid():N}")
            .Options);
}
