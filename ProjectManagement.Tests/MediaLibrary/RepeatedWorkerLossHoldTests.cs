using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// A job dead-lettered because it repeatedly took its worker down must not be requeued
/// automatically. A crashing classification never records its classifier version, so the
/// stale-classifier refresh in catalogue synchronisation and in external scans would
/// otherwise reset it on every pass and the image would keep crashing the server.
/// </summary>
public sealed class RepeatedWorkerLossHoldTests
{
    [Fact]
    public void Repeated_worker_loss_is_permanent_and_held()
    {
        var job = new MediaProcessingJob
        {
            Status = MediaProcessingJobStatus.DeadLetter,
            FailureCode = MediaProcessingFailurePolicy.RepeatedWorkerLossCode
        };

        Assert.True(MediaProcessingFailurePolicy.IsPermanentFailureCode(job.FailureCode));
        Assert.False(MediaProcessingFailurePolicy.IsRecoverableFailureCode(job.FailureCode));
        Assert.DoesNotContain(job.FailureCode, MediaProcessingFailurePolicy.RecoverableFailureCodeNames);
        Assert.True(MediaProcessingFailurePolicy.IsHeldForAdministrator(job));

        job.Status = MediaProcessingJobStatus.Pending;
        Assert.False(MediaProcessingFailurePolicy.IsHeldForAdministrator(job));
    }

    [Fact]
    public async Task Catalogue_sync_does_not_force_requeue_a_held_job_for_a_stale_classifier()
    {
        await using var mediaDb = CreateMediaContext();
        var jobId = await SeedHeldJobAsync(mediaDb);
        var synchronizer = CreateSynchronizer(mediaDb);

        await synchronizer.QueueJobsAsync(
            new[] { 1L },
            MediaProcessingJobType.ClassifyMedia,
            forceRequeueAssetIds: new HashSet<long> { 1L },
            contentChangedAssetIds: new HashSet<long>(),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        await mediaDb.SaveChangesAsync();

        var job = await mediaDb.ProcessingJobs.AsNoTracking().SingleAsync(item => item.Id == jobId);
        Assert.Equal(MediaProcessingJobStatus.DeadLetter, job.Status);
        Assert.Equal(MediaProcessingFailurePolicy.RepeatedWorkerLossCode, job.FailureCode);
    }

    [Fact]
    public async Task Catalogue_sync_requeues_a_held_job_when_the_content_changed()
    {
        await using var mediaDb = CreateMediaContext();
        var jobId = await SeedHeldJobAsync(mediaDb);
        var synchronizer = CreateSynchronizer(mediaDb);

        await synchronizer.QueueJobsAsync(
            new[] { 1L },
            MediaProcessingJobType.ClassifyMedia,
            forceRequeueAssetIds: new HashSet<long> { 1L },
            contentChangedAssetIds: new HashSet<long> { 1L },
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        await mediaDb.SaveChangesAsync();

        var job = await mediaDb.ProcessingJobs.AsNoTracking().SingleAsync(item => item.Id == jobId);
        Assert.Equal(MediaProcessingJobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public async Task External_scan_classifier_refresh_leaves_a_held_job_alone()
    {
        await using var mediaDb = CreateMediaContext();
        var jobId = await SeedHeldJobAsync(mediaDb);
        var scanner = CreateScanner(mediaDb);
        var asset = await mediaDb.Assets.SingleAsync();

        await scanner.QueueJobsAsync(
            new[] { asset },
            MediaProcessingJobType.ClassifyMedia,
            respectAdministratorHold: true,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var job = await mediaDb.ProcessingJobs.AsNoTracking().SingleAsync(item => item.Id == jobId);
        Assert.Equal(MediaProcessingJobStatus.DeadLetter, job.Status);

        // Changed or restored content is queued as before.
        await scanner.QueueJobsAsync(
            new[] { asset },
            MediaProcessingJobType.ClassifyMedia,
            respectAdministratorHold: false,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        job = await mediaDb.ProcessingJobs.AsNoTracking().SingleAsync(item => item.Id == jobId);
        Assert.Equal(MediaProcessingJobStatus.Pending, job.Status);
    }

    private static async Task<long> SeedHeldJobAsync(MediaLibraryDbContext db)
    {
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, classifierVersion: null));
        var job = ReclassifySeed.Job(1, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.DeadLetter, attempts: 5);
        job.FailureCode = MediaProcessingFailurePolicy.RepeatedWorkerLossCode;
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return job.Id;
    }

    private static PrismMediaCatalogueSynchronizer CreateSynchronizer(MediaLibraryDbContext mediaDb)
    {
        var applicationDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"hold-app-{Guid.NewGuid():N}")
            .Options);
        return new PrismMediaCatalogueSynchronizer(
            applicationDb,
            mediaDb,
            Options.Create(new MediaLibraryOptions()),
            new RecordingInvalidation(),
            new PrismMediaSourceSnapshotService(applicationDb),
            new PrismMediaSynchronizationGate(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrismMediaCatalogueSynchronizer>.Instance);
    }

    private static FileSystemMediaSourceScanner CreateScanner(MediaLibraryDbContext mediaDb)
        => new(
            mediaDb,
            Options.Create(new MediaLibraryOptions()),
            new FileSystemPathResolver(),
            new SafeFileEnumerator(NullLogger<SafeFileEnumerator>.Instance),
            new UnusedMetadataReader(),
            new RecordingInvalidation(),
            NullLogger<FileSystemMediaSourceScanner>.Instance);

    private static MediaLibraryDbContext CreateMediaContext()
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"hold-media-{Guid.NewGuid():N}")
            .Options);

    private sealed class UnusedMetadataReader : IMediaMetadataReader
    {
        public Task<MediaFileMetadata> ReadAsync(string path, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaFileMetadata> ReadAsync(MediaContentDescriptor content, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
