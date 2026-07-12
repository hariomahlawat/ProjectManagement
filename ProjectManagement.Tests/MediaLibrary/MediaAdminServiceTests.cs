using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Admin;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaAdminServiceTests
{
    [Fact]
    public async Task SourceStateChange_RejectsStaleConcurrencyToken()
    {
        await using var mediaDb = CreateMediaDb();
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var source = CreateSource(now);
        mediaDb.Sources.Add(source);
        await mediaDb.SaveChangesAsync();

        var service = new MediaSourceAdminService(
            mediaDb,
            Options.Create(new MediaLibraryOptions
            {
                Enabled = true,
                Catalogue = new MediaCatalogueOptions { Enabled = true },
                ExternalSources = new ExternalMediaSourcesOptions { Enabled = true }
            }),
            new HealthySourceProbe(now),
            new PassThroughPathResolver(),
            new AllowMediaAdminAccess(),
            new RecordingAdminAuditService(),
            new AdminTimeService(new FixedClock(now.AddMinutes(1))),
            CreateHttpContextAccessor(),
            NullLogger<MediaSourceAdminService>.Instance);

        var result = await service.SetStateAsync(
            source.Id,
            concurrencyToken: "stale-token",
            enabled: false,
            visible: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(MediaAdminErrorCodes.ConcurrencyConflict, result.ErrorCode);

        var persisted = await mediaDb.Sources.SingleAsync();
        Assert.True(persisted.IsEnabled);
        Assert.True(persisted.IsVisibleInLibrary);
    }

    [Fact]
    public void SourceConcurrencyToken_IgnoresRuntimeHealthChanges()
    {
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var source = CreateSource(now);
        var before = MediaSourceAdminConcurrency.Create(source);

        source.HealthStatus = "Unavailable";
        source.HealthMessage = "The share is temporarily offline.";
        source.LastHealthCheckedAtUtc = now.AddMinutes(5);
        source.ScanStatus = "Running";
        source.UpdatedAtUtc = now.AddMinutes(5);

        Assert.Equal(before, MediaSourceAdminConcurrency.Create(source));
    }

    [Fact]
    public void SourceConcurrencyToken_ChangesWhenAdministratorConfigurationChanges()
    {
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var source = CreateSource(now);
        var before = MediaSourceAdminConcurrency.Create(source);

        source.IsVisibleInLibrary = false;

        Assert.NotEqual(before, MediaSourceAdminConcurrency.Create(source));
    }

    [Fact]
    public async Task RetryJob_ResetsFailedJobAndRecordsAudit()
    {
        await using var mediaDb = CreateMediaDb();
        await using var applicationDb = CreateApplicationDb();
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var source = CreateSource(now);
        var asset = CreateAsset(source, now);
        var job = new MediaProcessingJob
        {
            MediaAsset = asset,
            JobType = MediaProcessingJobType.AnalyseAsset,
            Status = MediaProcessingJobStatus.Failed,
            AttemptCount = 3,
            MaxAttempts = 5,
            AvailableAfterUtc = now,
            FailureCode = nameof(IOException),
            FailureMessage = "Transient read failure.",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        mediaDb.ProcessingJobs.Add(job);
        await mediaDb.SaveChangesAsync();

        var audit = new RecordingAdminAuditService();
        var service = new MediaQueueAdminService(
            mediaDb,
            applicationDb,
            new AllowMediaAdminAccess(),
            audit,
            new AdminTimeService(new FixedClock(now.AddMinutes(5))),
            new RecordingOutboxSignal(),
            CreateHttpContextAccessor(),
            NullLogger<MediaQueueAdminService>.Instance);

        var result = await service.RetryJobAsync(job.Id, forcePermanent: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        var persisted = await mediaDb.ProcessingJobs.SingleAsync();
        Assert.Equal(MediaProcessingJobStatus.Pending, persisted.Status);
        Assert.Equal(0, persisted.AttemptCount);
        Assert.Null(persisted.FailureCode);
        Assert.Null(persisted.FailureMessage);
        Assert.Contains(audit.Entries, entry => entry.Action == "MediaQueueItemRetried");
    }

    [Fact]
    public async Task RetryJob_RejectsRunningJob()
    {
        await using var mediaDb = CreateMediaDb();
        await using var applicationDb = CreateApplicationDb();
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var source = CreateSource(now);
        var asset = CreateAsset(source, now);
        var job = new MediaProcessingJob
        {
            MediaAsset = asset,
            JobType = MediaProcessingJobType.AnalyseAsset,
            Status = MediaProcessingJobStatus.Running,
            AttemptCount = 1,
            MaxAttempts = 5,
            AvailableAfterUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        mediaDb.ProcessingJobs.Add(job);
        await mediaDb.SaveChangesAsync();

        var service = new MediaQueueAdminService(
            mediaDb,
            applicationDb,
            new AllowMediaAdminAccess(),
            new RecordingAdminAuditService(),
            new AdminTimeService(new FixedClock(now)),
            new RecordingOutboxSignal(),
            CreateHttpContextAccessor(),
            NullLogger<MediaQueueAdminService>.Instance);

        var result = await service.RetryJobAsync(job.Id, forcePermanent: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(MediaAdminErrorCodes.QueueItemNotRetryable, result.ErrorCode);
    }

    [Theory]
    [InlineData(MediaAvailabilityStatus.SourceMissing, "Source file missing")]
    [InlineData(MediaAvailabilityStatus.AccessDenied, "Access denied")]
    [InlineData(MediaAvailabilityStatus.TemporarilyUnavailable, "Temporarily unavailable")]
    public void AvailabilityStatusLabel_UsesOperationalLanguage(
        MediaAvailabilityStatus status,
        string expected)
    {
        Assert.Equal(expected, MediaAdminDisplay.AvailabilityStatusLabel(status));
    }

    private static MediaLibraryDbContext CreateMediaDb()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaLibraryDbContext(options);
    }

    private static ApplicationDbContext CreateApplicationDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static MediaLibrarySource CreateSource(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Key = "external-test",
        Name = "External test",
        SourceType = MediaLibrarySourceType.FileSystem,
        RootPath = Path.GetTempPath(),
        IsEnabled = true,
        IsVisibleInLibrary = true,
        IsReadOnly = true,
        IncludeSubfolders = true,
        AllowedExtensionsJson = "[\".jpg\"]",
        ScanStatus = "Idle",
        HealthStatus = "Reachable",
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static MediaAsset CreateAsset(MediaLibrarySource source, DateTimeOffset now) => new()
    {
        Source = source,
        SourceId = source.Id,
        Origin = MediaAssetOrigin.ExternalFile,
        Kind = MediaAssetKind.Photo,
        SourceEntityId = "external-test:1",
        OriginalFileName = "test.jpg",
        ContentType = "image/jpeg",
        ContextKey = "external-test",
        CollectionKey = "external-test",
        ContextTitle = "External test",
        ContextSubtitle = string.Empty,
        SourceLabel = "External test",
        Title = "test.jpg",
        MediaDateUtc = now,
        IndexedAtUtc = now,
        LastSeenAtUtc = now,
        LastSeenScanId = Guid.NewGuid(),
        IsAvailable = true,
        AvailabilityStatus = MediaAvailabilityStatus.Available
    };

    private static HttpContextAccessor CreateHttpContextAccessor()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "media-admin-test" };
        return new HttpContextAccessor { HttpContext = context };
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class AllowMediaAdminAccess : IMediaAdminAccessService
    {
        public Task<bool> IsAuthorizedAsync(string policy, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class RecordingAdminAuditService : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutboxSignal : IPrismMediaOutboxSignal
    {
        public int PulseCount { get; private set; }
        public void Pulse() => PulseCount++;
        public Task WaitAsync(TimeSpan maximumDelay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HealthySourceProbe : IFileSystemSourceHealthService
    {
        private readonly DateTimeOffset _checkedAt;
        public HealthySourceProbe(DateTimeOffset checkedAt) => _checkedAt = checkedAt;

        public Task<FileSystemSourceHealth> TestAsync(
            string rootPath,
            bool includeSubfolders,
            IReadOnlyCollection<string> allowedExtensions,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FileSystemSourceHealth(
                true,
                "Local folder",
                1,
                "The folder is reachable.",
                _checkedAt));
    }

    private sealed class PassThroughPathResolver : IFileSystemPathResolver
    {
        public string ResolveRoot(string configuredRoot) => Path.GetFullPath(configuredRoot);
        public string ResolveAssetPath(string rootPath, string relativePath) => Path.Combine(rootPath, relativePath);
        public string ToRelativePath(string rootPath, string fullPath) => Path.GetRelativePath(rootPath, fullPath);
        public string DescribePathKind(string configuredRoot) => "Local folder";
    }
}
