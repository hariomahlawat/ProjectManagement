using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Features.MediaLibrary.Outbox;

public interface IPrismActivityMediaIngestionService
{
    Task ProcessAsync(PrismMediaOutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Idempotently projects Activity image attachments into the persistent media catalogue.
/// Each operation is deliberately targeted; full-source reconciliation remains a repair path.
/// </summary>
public sealed class PrismActivityMediaIngestionService : IPrismActivityMediaIngestionService
{
    // Shared by targeted ingestion and full reconciliation across all application instances.
    public const long CatalogueAdvisoryLockKey = 5_065_249_836_774_113_021L;

    private readonly ApplicationDbContext _applicationDb;
    private readonly MediaLibraryDbContext _mediaDb;
    private readonly IMediaSourceBootstrapper _bootstrapper;
    private readonly IMediaContentChangeInvalidationService _contentInvalidation;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<PrismActivityMediaIngestionService> _logger;

    public PrismActivityMediaIngestionService(
        ApplicationDbContext applicationDb,
        MediaLibraryDbContext mediaDb,
        IMediaSourceBootstrapper bootstrapper,
        IMediaContentChangeInvalidationService contentInvalidation,
        IOptions<MediaLibraryOptions> options,
        ILogger<PrismActivityMediaIngestionService> logger)
    {
        _applicationDb = applicationDb ?? throw new ArgumentNullException(nameof(applicationDb));
        _mediaDb = mediaDb ?? throw new ArgumentNullException(nameof(mediaDb));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _contentInvalidation = contentInvalidation ?? throw new ArgumentNullException(nameof(contentInvalidation));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ProcessAsync(PrismMediaOutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.EventType switch
        {
            PrismMediaOutboxEventType.ActivityPhotoUpsert => UpsertPhotoAsync(
                message.AttachmentId,
                message.StorageKey,
                cancellationToken),
            PrismMediaOutboxEventType.ActivityPhotoRemoved => RemovePhotoAsync(
                message.AttachmentId,
                message.StorageKey,
                cancellationToken),
            PrismMediaOutboxEventType.ActivityMetadataRefresh => RefreshActivityAsync(
                RequireActivityId(message),
                cancellationToken),
            PrismMediaOutboxEventType.ActivityDeleted => RemoveActivityAsync(
                RequireActivityId(message),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported media outbox event {message.EventType}.")
        };
    }

    private async Task UpsertPhotoAsync(
        int? attachmentId,
        string? storageKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadAttachmentAsync(attachmentId, storageKey, cancellationToken);
        if (snapshot is null || snapshot.ActivityIsDeleted
            || !ActivityAttachmentClassifier.IsPhoto(snapshot.OriginalFileName, snapshot.ContentType))
        {
            await RemovePhotoAsync(attachmentId, storageKey, cancellationToken);
            return;
        }

        await EnsureSourceAsync(cancellationToken);
        await using var transaction = await _mediaDb.Database.BeginTransactionAsync(cancellationToken);
        await AcquireCatalogueLockAsync(cancellationToken);

        var source = await GetPrismSourceAsync(cancellationToken);
        var sourceEntityId = ActivitySourceEntityId(snapshot.Id);
        var asset = await _mediaDb.Assets
            .SingleOrDefaultAsync(item => item.SourceId == source.Id
                                          && item.SourceEntityId == sourceEntityId,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var fingerprint = BuildFingerprint(snapshot);
        var isNew = asset is null;
        MediaContentChangeSnapshot? change = null;

        if (asset is null)
        {
            asset = new MediaAsset
            {
                SourceId = source.Id,
                SourceEntityId = sourceEntityId,
                IndexedAtUtc = now,
                DerivativeStatus = MediaProcessingStatus.Pending,
                AnalysisStatus = _options.Classification.Enabled
                    ? MediaProcessingStatus.Pending
                    : MediaProcessingStatus.NotRequested,
                Classification = MediaClassification.Unknown,
                PredictedClassification = MediaClassification.Unknown,
                ClassificationDecisionStatus = MediaClassificationDecisionStatus.NotProcessed,
                FaceAnalysisStatus = MediaProcessingStatus.NotRequested
            };
            _mediaDb.Assets.Add(asset);
        }
        else if (!string.Equals(asset.QuickFingerprint, fingerprint, StringComparison.Ordinal))
        {
            change = _contentInvalidation.ResetAsset(
                asset,
                fingerprint,
                MediaAssetKind.Photo,
                _options.Classification.Enabled);
        }

        Map(asset, snapshot, fingerprint, now);
        await _mediaDb.SaveChangesAsync(cancellationToken);

        if (change is not null)
        {
            await _contentInvalidation.RetireDerivedIntelligenceAsync(
                new[] { change },
                now,
                cancellationToken);
        }

        var needsDerivatives = asset.DerivativeStatus == MediaProcessingStatus.Pending;
        var needsClassification = _options.Classification.Enabled
                                  && !asset.ClassificationIsManual
                                  && (asset.AnalysisStatus is MediaProcessingStatus.NotRequested
                                      or MediaProcessingStatus.Pending
                                      or MediaProcessingStatus.Failed
                                      || asset.ClassifierVersion != MediaClassifier.ClassifierVersion);

        // AnalyseAsset builds derivatives and performs classification in one pass. Queueing a
        // separate ClassifyMedia job at the same time wastes inference work and can race the
        // derivative write. A classification-only job is appropriate only when derivatives are
        // already current.
        if (needsDerivatives)
        {
            await EnsureProcessingJobAsync(asset.Id, MediaProcessingJobType.AnalyseAsset, now, cancellationToken);
            await RemoveNonRunningJobAsync(asset.Id, MediaProcessingJobType.ClassifyMedia, cancellationToken);
        }
        else if (needsClassification)
        {
            await EnsureProcessingJobAsync(asset.Id, MediaProcessingJobType.ClassifyMedia, now, cancellationToken);
        }

        source.IndexedAssetCount = await _mediaDb.Assets.LongCountAsync(
            item => item.SourceId == source.Id && item.IsAvailable && !item.IsDeleted,
            cancellationToken);
        source.ConfigurationFingerprint = null;
        source.ScanStatus = "Ingestion active";
        source.ScanRequestedAtUtc = now;
        source.LastError = null;
        source.UpdatedAtUtc = now;

        await _mediaDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Activity photo {AttachmentId} was {Action} in the media catalogue as asset {AssetId}",
            snapshot.Id,
            isNew ? "created" : change is null ? "refreshed" : "updated",
            asset.Id);
    }

    private async Task RefreshActivityAsync(int activityId, CancellationToken cancellationToken)
    {
        var activity = await _applicationDb.Activities
            .AsNoTracking()
            .Where(item => item.Id == activityId)
            .Select(item => new { item.Id, item.IsDeleted })
            .SingleOrDefaultAsync(cancellationToken);

        if (activity is null || activity.IsDeleted)
        {
            await RemoveActivityAsync(activityId, cancellationToken);
            return;
        }

        var photoQuery = _applicationDb.ActivityAttachments
            .AsNoTracking()
            .Where(item => item.ActivityId == activityId)
            .Where(ActivityAttachmentClassifier.IsPhotoExpression);
        var photos = await photoQuery
            .Select(item => new { item.Id, item.StorageKey })
            .ToListAsync(cancellationToken);

        foreach (var photo in photos)
        {
            await UpsertPhotoAsync(photo.Id, photo.StorageKey, cancellationToken);
        }

        await RemoveOrphanedActivityAssetsAsync(
            activityId,
            photos.Select(item => item.Id).ToHashSet(),
            cancellationToken);
    }

    private async Task RemovePhotoAsync(
        int? attachmentId,
        string? storageKey,
        CancellationToken cancellationToken)
    {
        await EnsureSourceAsync(cancellationToken);
        await using var transaction = await _mediaDb.Database.BeginTransactionAsync(cancellationToken);
        await AcquireCatalogueLockAsync(cancellationToken);

        var source = await GetPrismSourceAsync(cancellationToken);
        MediaAsset? asset = null;
        if (attachmentId is > 0)
        {
            var sourceEntityId = ActivitySourceEntityId(attachmentId.Value);
            asset = await _mediaDb.Assets.SingleOrDefaultAsync(
                item => item.SourceId == source.Id && item.SourceEntityId == sourceEntityId,
                cancellationToken);
        }

        if (asset is null && !string.IsNullOrWhiteSpace(storageKey))
        {
            // StorageKey is the durable recovery locator for events created before an identity
            // value was generated, and for hard-delete events after the source row has gone.
            var attachment = await _applicationDb.ActivityAttachments
                .AsNoTracking()
                .Where(item => item.StorageKey == storageKey)
                .Select(item => (int?)item.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (attachment is > 0)
            {
                var sourceEntityId = ActivitySourceEntityId(attachment.Value);
                asset = await _mediaDb.Assets.SingleOrDefaultAsync(
                    item => item.SourceId == source.Id && item.SourceEntityId == sourceEntityId,
                    cancellationToken);
            }

            asset ??= await _mediaDb.Assets.SingleOrDefaultAsync(
                item => item.SourceId == source.Id
                        && item.Origin == MediaAssetOrigin.ActivityPhoto
                        && item.RelativePath == storageKey,
                cancellationToken);
        }

        if (asset is not null)
        {
            await RetireAndHideAsync(asset, "Activity photo removed from its source record.", cancellationToken);
            await UpdateSourceCountAsync(source, cancellationToken);
        }

        await _mediaDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RemoveActivityAsync(int activityId, CancellationToken cancellationToken)
    {
        await EnsureSourceAsync(cancellationToken);
        await using var transaction = await _mediaDb.Database.BeginTransactionAsync(cancellationToken);
        await AcquireCatalogueLockAsync(cancellationToken);

        var source = await GetPrismSourceAsync(cancellationToken);
        var parentId = activityId.ToString(CultureInfo.InvariantCulture);
        var assets = await _mediaDb.Assets
            .Where(item => item.SourceId == source.Id
                           && item.Origin == MediaAssetOrigin.ActivityPhoto
                           && item.ParentEntityId == parentId
                           && !item.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var asset in assets)
        {
            await RetireAndHideAsync(asset, "Activity was deleted from PRISM.", cancellationToken);
        }

        await UpdateSourceCountAsync(source, cancellationToken);
        await _mediaDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RemoveOrphanedActivityAssetsAsync(
        int activityId,
        IReadOnlySet<int> activeAttachmentIds,
        CancellationToken cancellationToken)
    {
        await EnsureSourceAsync(cancellationToken);
        await using var transaction = await _mediaDb.Database.BeginTransactionAsync(cancellationToken);
        await AcquireCatalogueLockAsync(cancellationToken);

        var source = await GetPrismSourceAsync(cancellationToken);
        var parentId = activityId.ToString(CultureInfo.InvariantCulture);
        var candidates = await _mediaDb.Assets
            .Where(item => item.SourceId == source.Id
                           && item.Origin == MediaAssetOrigin.ActivityPhoto
                           && item.ParentEntityId == parentId
                           && !item.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var asset in candidates)
        {
            var parsed = ParseAttachmentId(asset.SourceEntityId);
            if (!parsed.HasValue || activeAttachmentIds.Contains(parsed.Value))
            {
                continue;
            }

            await RetireAndHideAsync(asset, "Activity attachment no longer exists.", cancellationToken);
        }

        await UpdateSourceCountAsync(source, cancellationToken);
        await _mediaDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RetireAndHideAsync(
        MediaAsset asset,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (asset.Faces.Count > 0 || await _mediaDb.Faces.AnyAsync(face => face.MediaAssetId == asset.Id, cancellationToken))
        {
            await _contentInvalidation.RetireFaceIntelligenceAsync(
                new[] { asset.Id },
                "ActivitySourceRemoved",
                "system:activity-media-outbox",
                reason,
                now,
                cancellationToken);
        }

        var nonRunningJobs = await _mediaDb.ProcessingJobs
            .Where(job => job.MediaAssetId == asset.Id
                          && job.Status != MediaProcessingJobStatus.Running)
            .ToListAsync(cancellationToken);
        if (nonRunningJobs.Count > 0)
        {
            _mediaDb.ProcessingJobs.RemoveRange(nonRunningJobs);
        }

        asset.IsAvailable = false;
        asset.IsDeleted = true;
        asset.AvailabilityStatus = MediaAvailabilityStatus.SourceMissing;
        asset.UnavailableReason = reason;
        asset.UnavailableSinceUtc ??= now;
        asset.LastAvailabilityCheckUtc = now;
        asset.LastSeenAtUtc = now;
        asset.DerivativeStatus = MediaProcessingStatus.NotRequested;
        asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
        asset.FaceAnalysisStatus = MediaProcessingStatus.NotRequested;
    }

    private async Task<ActivityPhotoSnapshot?> LoadAttachmentAsync(
        int? attachmentId,
        string? storageKey,
        CancellationToken cancellationToken)
    {
        var query = _applicationDb.ActivityAttachments.AsNoTracking();
        if (attachmentId is > 0)
        {
            query = query.Where(item => item.Id == attachmentId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(storageKey))
        {
            query = query.Where(item => item.StorageKey == storageKey);
        }
        else
        {
            return null;
        }

        return await query
            .Select(item => new ActivityPhotoSnapshot(
                item.Id,
                item.ActivityId,
                item.Activity.IsDeleted,
                item.Activity.Title,
                item.Activity.ActivityType.Name,
                item.Activity.Location,
                item.Activity.ScheduledStartUtc,
                item.Activity.CreatedAtUtc,
                item.Activity.LastModifiedAtUtc,
                item.StorageKey,
                item.OriginalFileName,
                item.ContentType,
                item.FileSize,
                item.UploadedAtUtc,
                item.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static void Map(
        MediaAsset asset,
        ActivityPhotoSnapshot snapshot,
        string fingerprint,
        DateTimeOffset now)
    {
        asset.ParentEntityId = snapshot.ActivityId.ToString(CultureInfo.InvariantCulture);
        asset.Origin = MediaAssetOrigin.ActivityPhoto;
        asset.Kind = MediaAssetKind.Photo;
        asset.RelativePath = snapshot.StorageKey;
        asset.OriginalFileName = snapshot.OriginalFileName;
        asset.ContentType = snapshot.ContentType;
        asset.FileSizeBytes = snapshot.FileSize;
        asset.FileModifiedAtUtc = snapshot.UploadedAtUtc;
        asset.ContextKey = $"activity:{snapshot.ActivityId}";
        asset.CollectionKey = $"activity:{snapshot.ActivityId}";
        asset.ContextTitle = snapshot.ActivityTitle;
        asset.ContextSubtitle = string.IsNullOrWhiteSpace(snapshot.ActivityType)
            ? "Institutional activity"
            : snapshot.ActivityType;
        asset.SourceLabel = "Activity";
        asset.Title = snapshot.ActivityTitle;
        asset.Caption = snapshot.Location;
        asset.ProjectId = null;
        asset.MediaDateUtc = snapshot.ScheduledStartUtc ?? snapshot.UploadedAtUtc;
        asset.VersionToken = Convert.ToHexString(snapshot.RowVersion ?? Array.Empty<byte>());
        asset.IsCover = false;
        asset.SortOrder = snapshot.UploadedAtUtc.UtcDateTime.Ticks;
        asset.QuickFingerprint = fingerprint;
        asset.IsAvailable = true;
        asset.AvailabilityStatus = MediaAvailabilityStatus.Available;
        asset.UnavailableReason = null;
        asset.UnavailableSinceUtc = null;
        asset.LastAvailabilityCheckUtc = now;
        asset.IsArchived = false;
        asset.IsDeleted = false;
        asset.LastSeenAtUtc = now;
        asset.LastSeenScanId = Guid.Empty;
    }

    private async Task EnsureProcessingJobAsync(
        long assetId,
        MediaProcessingJobType jobType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await _mediaDb.ProcessingJobs.SingleOrDefaultAsync(
            item => item.MediaAssetId == assetId && item.JobType == jobType,
            cancellationToken);

        if (job is null)
        {
            _mediaDb.ProcessingJobs.Add(new MediaProcessingJob
            {
                MediaAssetId = assetId,
                JobType = jobType,
                Status = MediaProcessingJobStatus.Pending,
                AttemptCount = 0,
                MaxAttempts = _options.Processing.MaxAttempts,
                AvailableAfterUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            return;
        }

        if (job.Status == MediaProcessingJobStatus.Running
            && job.LockExpiresAtUtc is { } lockExpiry
            && lockExpiry > now)
        {
            return;
        }

        job.Status = MediaProcessingJobStatus.Pending;
        job.AttemptCount = 0;
        job.MaxAttempts = _options.Processing.MaxAttempts;
        job.AvailableAfterUtc = now;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
        job.LockedBy = null;
        job.LockExpiresAtUtc = null;
        job.FailureCode = null;
        job.FailureMessage = null;
        job.UpdatedAtUtc = now;
    }


    private async Task RemoveNonRunningJobAsync(
        long assetId,
        MediaProcessingJobType jobType,
        CancellationToken cancellationToken)
    {
        var jobs = await _mediaDb.ProcessingJobs
            .Where(item => item.MediaAssetId == assetId
                           && item.JobType == jobType
                           && item.Status != MediaProcessingJobStatus.Running)
            .ToListAsync(cancellationToken);
        if (jobs.Count > 0)
        {
            _mediaDb.ProcessingJobs.RemoveRange(jobs);
        }
    }

    private async Task EnsureSourceAsync(CancellationToken cancellationToken)
        => await _bootstrapper.EnsureConfiguredSourcesAsync(cancellationToken);

    private Task<MediaLibrarySource> GetPrismSourceAsync(CancellationToken cancellationToken)
        => _mediaDb.Sources.SingleAsync(
            item => item.Key == MediaSourceBootstrapper.PrismSourceKey && !item.IsDeleted,
            cancellationToken);

    private async Task UpdateSourceCountAsync(
        MediaLibrarySource source,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        source.IndexedAssetCount = await _mediaDb.Assets.LongCountAsync(
            item => item.SourceId == source.Id && item.IsAvailable && !item.IsDeleted,
            cancellationToken);
        source.ConfigurationFingerprint = null;
        source.ScanStatus = "Ingestion active";
        source.ScanRequestedAtUtc = now;
        source.UpdatedAtUtc = now;
    }

    private async Task AcquireCatalogueLockAsync(CancellationToken cancellationToken)
    {
        if ((_mediaDb.Database.ProviderName ?? string.Empty)
            .Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await _mediaDb.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_xact_lock({CatalogueAdvisoryLockKey})",
                cancellationToken);
        }
    }

    private static int RequireActivityId(PrismMediaOutboxMessage message)
        => message.ActivityId is > 0
            ? message.ActivityId.Value
            : throw new InvalidOperationException($"Outbox event {message.EventId} has no ActivityId.");

    private static string ActivitySourceEntityId(int attachmentId)
        => $"activity-photo:{attachmentId}";

    private static int? ParseAttachmentId(string sourceEntityId)
    {
        const string prefix = "activity-photo:";
        return sourceEntityId.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(sourceEntityId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static string BuildFingerprint(ActivityPhotoSnapshot snapshot)
    {
        var versionToken = Convert.ToHexString(snapshot.RowVersion ?? Array.Empty<byte>());
        var activityModified = snapshot.ActivityLastModifiedAtUtc ?? snapshot.ActivityCreatedAtUtc;
        return $"{versionToken}:{snapshot.UploadedAtUtc.UtcDateTime.Ticks}:{activityModified.UtcDateTime.Ticks}:{snapshot.StorageKey}:{snapshot.FileSize}";
    }

    private sealed record ActivityPhotoSnapshot(
        int Id,
        int ActivityId,
        bool ActivityIsDeleted,
        string ActivityTitle,
        string ActivityType,
        string? Location,
        DateTimeOffset? ScheduledStartUtc,
        DateTimeOffset ActivityCreatedAtUtc,
        DateTimeOffset? ActivityLastModifiedAtUtc,
        string StorageKey,
        string OriginalFileName,
        string ContentType,
        long FileSize,
        DateTimeOffset UploadedAtUtc,
        byte[] RowVersion);
}
