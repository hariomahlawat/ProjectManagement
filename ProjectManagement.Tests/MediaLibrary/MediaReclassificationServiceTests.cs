using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Features.MediaLibrary.Admin;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Fourth admin review, PR 3 (C2): "Sort older images again" runs in
/// <see cref="MediaReclassificationService"/> and never resets a job a worker holds. The
/// row-lock race itself is covered against PostgreSQL in
/// <see cref="MediaReclassificationPostgresIntegrationTests"/>.
/// </summary>
public sealed class MediaReclassificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task QueuesOlderImages_ResetsTheirJobs_AndWritesTheAudits()
    {
        await using var db = CreateDb();
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.AddRange(
            ReclassifySeed.Asset(source, 1, "hybrid-media-v6"),
            ReclassifySeed.Asset(source, 2, null),
            ReclassifySeed.Asset(source, 3, MediaClassifier.ClassifierVersion),
            ReclassifySeed.Asset(source, 4, "hybrid-media-v6", manual: true));
        db.ProcessingJobs.AddRange(
            ReclassifySeed.Job(1, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Failed, attempts: 5),
            ReclassifySeed.Job(1, MediaProcessingJobType.DetectFaces, MediaProcessingJobStatus.Pending),
            ReclassifySeed.Job(1, MediaProcessingJobType.GenerateFaceEmbeddings, MediaProcessingJobStatus.Completed));
        await db.SaveChangesAsync();
        var invalidation = new RecordingInvalidation();
        var audit = new CapturingAudit();

        var result = await Service(db, invalidation, audit).QueueStaleAsync("admin-1", CancellationToken.None);

        Assert.Equal(new MediaReclassifyStaleResult(MediaReclassifyStaleOutcome.Queued, 2, 0, false), result);
        Assert.Equal(new long[] { 1, 2 }, invalidation.AssetIds);

        db.ChangeTracker.Clear();
        var assets = await db.Assets.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        Assert.All(assets.Take(2), asset =>
        {
            Assert.Null(asset.ClassifierVersion);
            Assert.Equal(MediaClassification.Unknown, asset.Classification);
            Assert.Equal("CLASSIFIER_VERSION_CHANGED", asset.ClassificationDecisionReasonCode);
        });
        Assert.Equal(MediaClassifier.ClassifierVersion, assets[2].ClassifierVersion);
        Assert.Equal("hybrid-media-v6", assets[3].ClassifierVersion);

        var jobs = await db.ProcessingJobs.AsNoTracking().ToListAsync();
        var classify = jobs.Where(j => j.JobType == MediaProcessingJobType.ClassifyMedia).OrderBy(j => j.MediaAssetId).ToList();
        Assert.Equal(new long[] { 1, 2 }, classify.Select(j => j.MediaAssetId));
        Assert.All(classify, job =>
        {
            Assert.Equal(MediaProcessingJobStatus.Pending, job.Status);
            Assert.Equal(0, job.AttemptCount);
            Assert.Null(job.LockedBy);
            Assert.Equal(Now, job.AvailableAfterUtc);
        });
        // Waiting face work is dropped; finished face work stays as history.
        Assert.DoesNotContain(jobs, j => j.JobType == MediaProcessingJobType.DetectFaces);
        Assert.Contains(jobs, j => j.JobType == MediaProcessingJobType.GenerateFaceEmbeddings);

        var audits = await db.ClassificationAudits.AsNoTracking().OrderBy(a => a.MediaAssetId).ToListAsync();
        Assert.Equal(new long[] { 1, 2 }, audits.Select(a => a.MediaAssetId));
        Assert.All(audits, a =>
        {
            Assert.Equal("admin-1", a.ChangedByUserId);
            Assert.Equal(Now, a.ChangedAtUtc);
            Assert.Equal($"Queued for classifier upgrade to {MediaClassifier.ClassifierVersion}.", a.Reason);
            Assert.Equal(MediaClassificationDecisionStatus.NotProcessed, a.NewDecisionStatus);
        });

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(MediaReclassificationService.AdminAuditAction, entry.Action);
        Assert.Equal("Admin.MediaIntelligence.Classifications", entry.Origin);
    }

    [Fact]
    public async Task LeavesImagesAWorkerHoldsAlone_AndCountsThem()
    {
        await using var db = CreateDb();
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.AddRange(
            ReclassifySeed.Asset(source, 1, "hybrid-media-v6"),
            ReclassifySeed.Asset(source, 2, "hybrid-media-v6"),
            ReclassifySeed.Asset(source, 3, "hybrid-media-v6"),
            ReclassifySeed.Asset(source, 4, "hybrid-media-v6"));
        db.ProcessingJobs.AddRange(
            ReclassifySeed.Job(1, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Running, "worker-a", Now.AddMinutes(10)),
            ReclassifySeed.Job(2, MediaProcessingJobType.AnalyseAsset, MediaProcessingJobStatus.Running, "worker-b", Now.AddMinutes(10)),
            // A lease that ran out is the worker's own recovery case, not a held job.
            ReclassifySeed.Job(3, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Running, "worker-c", Now.AddMinutes(-1)));
        await db.SaveChangesAsync();
        var audit = new CapturingAudit();

        var result = await Service(db, new RecordingInvalidation(), audit).QueueStaleAsync("admin-1", CancellationToken.None);

        Assert.Equal(new MediaReclassifyStaleResult(MediaReclassifyStaleOutcome.Queued, 2, 2, false), result);
        db.ChangeTracker.Clear();
        var held = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.MediaAssetId == 1);
        Assert.Equal(MediaProcessingJobStatus.Running, held.Status);
        Assert.Equal("worker-a", held.LockedBy);
        Assert.Equal("hybrid-media-v6", (await db.Assets.AsNoTracking().SingleAsync(a => a.Id == 1)).ClassifierVersion);
        Assert.Equal("hybrid-media-v6", (await db.Assets.AsNoTracking().SingleAsync(a => a.Id == 2)).ClassifierVersion);
        var recovered = await db.ProcessingJobs.AsNoTracking()
            .SingleAsync(j => j.MediaAssetId == 3 && j.JobType == MediaProcessingJobType.ClassifyMedia);
        Assert.Equal(MediaProcessingJobStatus.Pending, recovered.Status);
        Assert.Null(recovered.LockedBy);
        Assert.Equal(new long[] { 3, 4 }, (await db.ClassificationAudits.AsNoTracking().ToListAsync()).Select(a => a.MediaAssetId).Order());

        var entry = Assert.Single(audit.Entries);
        Assert.Contains("SkippedActive = 2", entry.After!.ToString());
    }

    [Fact]
    public async Task WhenEveryImageIsHeld_NothingChanges()
    {
        await using var db = CreateDb();
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, "hybrid-media-v6"));
        db.ProcessingJobs.Add(ReclassifySeed.Job(1, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Running, "worker-a", Now.AddMinutes(10)));
        await db.SaveChangesAsync();
        var audit = new CapturingAudit();

        var result = await Service(db, new RecordingInvalidation(), audit).QueueStaleAsync("admin-1", CancellationToken.None);

        Assert.Equal(MediaReclassifyStaleOutcome.AllBusy, result.Outcome);
        Assert.Empty(audit.Entries);
        Assert.Empty(await db.ClassificationAudits.ToListAsync());
    }

    // A reset image stays "stale" (ClassifierVersion null) until the worker sorts it, so running
    // this again used to re-reset the same first batch. Images already queued are left out.
    [Fact]
    public async Task ImagesAlreadyWaiting_AreLeftAlone()
    {
        await using var db = CreateDb();
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.AddRange(ReclassifySeed.Asset(source, 1, null), ReclassifySeed.Asset(source, 2, null));
        db.ProcessingJobs.AddRange(
            ReclassifySeed.Job(1, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Pending),
            ReclassifySeed.Job(2, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Pending));
        await db.SaveChangesAsync();

        var result = await Service(db, new RecordingInvalidation(), new CapturingAudit()).QueueStaleAsync("admin-1", CancellationToken.None);

        Assert.Equal(MediaReclassifyStaleOutcome.AllAlreadyWaiting, result.Outcome);
    }

    [Fact]
    public async Task WhenNothingIsOlder_SaysSo()
    {
        await using var db = CreateDb();
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, MediaClassifier.ClassifierVersion));
        await db.SaveChangesAsync();

        var result = await Service(db, new RecordingInvalidation(), new CapturingAudit()).QueueStaleAsync("admin-1", CancellationToken.None);

        Assert.Equal(MediaReclassifyStaleOutcome.NothingStale, result.Outcome);
    }

    [Fact]
    public async Task QueuesOneBatchAtATime()
    {
        await using var db = CreateDb();
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        for (var id = 1; id <= MediaReclassificationService.MaximumBatch + 1; id++)
        {
            db.Assets.Add(ReclassifySeed.Asset(source, id, "hybrid-media-v6"));
        }

        await db.SaveChangesAsync();
        var service = Service(db, new RecordingInvalidation(), new CapturingAudit());

        var first = await service.QueueStaleAsync("admin-1", CancellationToken.None);
        var second = await service.QueueStaleAsync("admin-1", CancellationToken.None);

        Assert.Equal(new MediaReclassifyStaleResult(MediaReclassifyStaleOutcome.Queued, MediaReclassificationService.MaximumBatch, 0, true), first);
        Assert.Equal(new MediaReclassifyStaleResult(MediaReclassifyStaleOutcome.Queued, 1, 0, false), second);
    }

    [Fact]
    public async Task AFailedAdminAudit_DoesNotUndoTheQueueing()
    {
        await using var db = CreateDb();
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, "hybrid-media-v6"));
        await db.SaveChangesAsync();

        var result = await Service(db, new RecordingInvalidation(), new CapturingAudit(fail: true)).QueueStaleAsync("admin-1", CancellationToken.None);

        Assert.Equal(1, result.Queued);
        Assert.Single(await db.ClassificationAudits.ToListAsync());
    }

    private static MediaReclassificationService Service(
        MediaLibraryDbContext db,
        IMediaContentChangeInvalidationService invalidation,
        IAdminAuditService audit) =>
        new(db, invalidation, audit, FakeClock.AtUtc(Now), NullLogger<MediaReclassificationService>.Instance);

    private static MediaLibraryDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-reclassify-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}

internal static class ReclassifySeed
{
    public static MediaLibrarySource Source() => new()
    {
        Id = Guid.NewGuid(),
        Key = $"reclassify-{Guid.NewGuid():N}"[..20],
        Name = "Reclassify test",
        SourceType = MediaLibrarySourceType.Prism,
        IsEnabled = true,
        IsVisibleInLibrary = true
    };

    public static MediaAsset Asset(MediaLibrarySource source, long id, string? classifierVersion, bool manual = false)
    {
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new MediaAsset
        {
            Id = id,
            SourceId = source.Id,
            Origin = MediaAssetOrigin.ProjectPhoto,
            Kind = MediaAssetKind.Photo,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = $"photo:{id}",
            ContextKey = "project:1",
            CollectionKey = "project:1",
            ContextTitle = "Test",
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = $"Photo {id}",
            OriginalFileName = $"photo-{id}.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = at,
            IndexedAtUtc = at,
            LastSeenAtUtc = at,
            ClassifierVersion = classifierVersion,
            ClassificationIsManual = manual,
            Classification = MediaClassification.Photograph,
            AnalysisStatus = MediaProcessingStatus.Ready,
            ClassificationDecisionStatus = manual
                ? MediaClassificationDecisionStatus.ManuallyConfirmed
                : MediaClassificationDecisionStatus.AutomaticallyAccepted
        };
    }

    public static MediaProcessingJob Job(
        long assetId,
        MediaProcessingJobType type,
        MediaProcessingJobStatus status,
        string? lockedBy = null,
        DateTimeOffset? lockExpires = null,
        int attempts = 0)
    {
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new MediaProcessingJob
        {
            MediaAssetId = assetId,
            JobType = type,
            Status = status,
            LockedBy = lockedBy,
            LockExpiresAtUtc = lockExpires,
            AttemptCount = attempts,
            AvailableAfterUtc = at,
            CreatedAtUtc = at,
            UpdatedAtUtc = at
        };
    }
}

internal sealed class RecordingInvalidation : IMediaContentChangeInvalidationService
{
    public List<long> AssetIds { get; } = new();

    public Task RetireFaceIntelligenceAsync(
        IReadOnlyCollection<long> assetIds,
        string action,
        string performedByUserId,
        string notes,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        AssetIds.AddRange(assetIds);
        return Task.CompletedTask;
    }

    public MediaContentChangeSnapshot ResetAsset(
        MediaAsset asset,
        string newFingerprint,
        MediaAssetKind newKind,
        bool classificationEnabled)
        => throw new NotSupportedException();

    public Task RetireDerivedIntelligenceAsync(
        IReadOnlyCollection<MediaContentChangeSnapshot> changes,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class CapturingAudit : IAdminAuditService
{
    private readonly bool _fail;

    public CapturingAudit(bool fail = false) => _fail = fail;

    public List<AdminAuditEntry> Entries { get; } = new();

    public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (_fail)
        {
            throw new InvalidOperationException("Audit store unavailable.");
        }

        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
