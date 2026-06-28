using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class PrismMediaCatalogueSynchronizer : IPrismMediaCatalogueSynchronizer
{
    private readonly ApplicationDbContext _applicationDb;
    private readonly MediaLibraryDbContext _mediaDb;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<PrismMediaCatalogueSynchronizer> _logger;

    public PrismMediaCatalogueSynchronizer(
        ApplicationDbContext applicationDb,
        MediaLibraryDbContext mediaDb,
        IOptions<MediaLibraryOptions> options,
        ILogger<PrismMediaCatalogueSynchronizer> logger)
    {
        _applicationDb = applicationDb ?? throw new ArgumentNullException(nameof(applicationDb));
        _mediaDb = mediaDb ?? throw new ArgumentNullException(nameof(mediaDb));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var source = await _mediaDb.Sources
            .SingleAsync(item => item.Key == MediaSourceBootstrapper.PrismSourceKey, cancellationToken);

        var scanId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        source.LastScanStartedAtUtc = now;
        source.ScanStatus = "Scanning";
        source.LastError = null;
        await _mediaDb.SaveChangesAsync(cancellationToken);

        try
        {
            var existing = await _mediaDb.Assets
                .Where(asset => asset.SourceId == source.Id)
                .ToDictionaryAsync(asset => asset.SourceEntityId, StringComparer.Ordinal, cancellationToken);

            var photos = await _applicationDb.ProjectPhotos
                .AsNoTracking()
                .Where(photo => !photo.Project.IsDeleted)
                .Select(photo => new
                {
                    photo.Id,
                    photo.ProjectId,
                    ProjectName = photo.Project.Name,
                    photo.Caption,
                    photo.OriginalFileName,
                    photo.ContentType,
                    photo.Width,
                    photo.Height,
                    photo.Ordinal,
                    photo.IsCover,
                    photo.Version,
                    photo.CreatedUtc,
                    photo.UpdatedUtc
                })
                .ToListAsync(cancellationToken);

            foreach (var row in photos)
            {
                Upsert(existing, source.Id, scanId, now, new AssetValues(
                    $"project-photo:{row.Id}",
                    row.ProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MediaAssetOrigin.ProjectPhoto,
                    MediaAssetKind.Photo,
                    row.OriginalFileName,
                    row.ContentType,
                    $"project:{row.ProjectId}",
                    $"project:{row.ProjectId}",
                    row.ProjectName,
                    "Project media",
                    "Project",
                    string.IsNullOrWhiteSpace(row.Caption) ? row.OriginalFileName : row.Caption,
                    row.Caption,
                    row.ProjectId,
                    ToUtcOffset(row.CreatedUtc),
                    row.Width,
                    row.Height,
                    null,
                    row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row.IsCover,
                    row.Ordinal,
                    $"{row.Version}:{row.UpdatedUtc.Ticks}"));
            }

            var videos = await _applicationDb.ProjectVideos
                .AsNoTracking()
                .Where(video => !video.Project.IsDeleted)
                .Select(video => new
                {
                    video.Id,
                    video.ProjectId,
                    ProjectName = video.Project.Name,
                    video.Title,
                    video.Description,
                    video.OriginalFileName,
                    video.ContentType,
                    video.FileSize,
                    video.DurationSeconds,
                    video.Ordinal,
                    video.IsFeatured,
                    video.Version,
                    video.CreatedUtc,
                    video.UpdatedUtc
                })
                .ToListAsync(cancellationToken);

            foreach (var row in videos)
            {
                var entity = Upsert(existing, source.Id, scanId, now, new AssetValues(
                    $"project-video:{row.Id}",
                    row.ProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MediaAssetOrigin.ProjectVideo,
                    MediaAssetKind.Video,
                    row.OriginalFileName,
                    row.ContentType,
                    $"project:{row.ProjectId}",
                    $"project:{row.ProjectId}",
                    row.ProjectName,
                    "Project media",
                    "Project video",
                    string.IsNullOrWhiteSpace(row.Title) ? row.OriginalFileName : row.Title,
                    row.Description,
                    row.ProjectId,
                    ToUtcOffset(row.CreatedUtc),
                    null,
                    null,
                    row.DurationSeconds,
                    row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row.IsFeatured,
                    row.Ordinal,
                    $"{row.Version}:{row.UpdatedUtc.Ticks}"));
                entity.FileSizeBytes = row.FileSize;
            }

            var visitPhotos = await _applicationDb.VisitPhotos
                .AsNoTracking()
                .Select(photo => new
                {
                    photo.Id,
                    photo.VisitId,
                    VisitorName = photo.Visit!.VisitorName,
                    VisitType = photo.Visit.VisitType != null ? photo.Visit.VisitType.Name : null,
                    photo.Visit.DateOfVisit,
                    photo.Caption,
                    photo.StorageKey,
                    photo.ContentType,
                    photo.Width,
                    photo.Height,
                    photo.VersionStamp,
                    photo.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

            foreach (var row in visitPhotos)
            {
                var date = new DateTimeOffset(row.DateOfVisit.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                Upsert(existing, source.Id, scanId, now, new AssetValues(
                    $"visit-photo:{row.Id}",
                    row.VisitId.ToString(),
                    MediaAssetOrigin.VisitPhoto,
                    MediaAssetKind.Photo,
                    Path.GetFileName(row.StorageKey),
                    row.ContentType,
                    $"visit:{row.VisitId}",
                    $"visit:{row.VisitId}",
                    $"Visit of {row.VisitorName}",
                    string.IsNullOrWhiteSpace(row.VisitType) ? "Visit to SDD" : row.VisitType,
                    "Visit",
                    string.IsNullOrWhiteSpace(row.Caption) ? $"Visit of {row.VisitorName}" : row.Caption,
                    row.Caption,
                    null,
                    date,
                    row.Width,
                    row.Height,
                    null,
                    row.VersionStamp,
                    false,
                    row.CreatedAtUtc.UtcDateTime.Ticks,
                    row.VersionStamp));
            }

            var eventPhotos = await _applicationDb.SocialMediaEventPhotos
                .AsNoTracking()
                .Select(photo => new
                {
                    photo.Id,
                    EventId = photo.SocialMediaEventId,
                    EventTitle = photo.SocialMediaEvent!.Title,
                    EventType = photo.SocialMediaEvent.SocialMediaEventType != null ? photo.SocialMediaEvent.SocialMediaEventType.Name : null,
                    photo.SocialMediaEvent.DateOfEvent,
                    photo.Caption,
                    photo.StorageKey,
                    photo.ContentType,
                    photo.Width,
                    photo.Height,
                    photo.IsCover,
                    photo.VersionStamp,
                    photo.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

            foreach (var row in eventPhotos)
            {
                var date = new DateTimeOffset(row.DateOfEvent.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                Upsert(existing, source.Id, scanId, now, new AssetValues(
                    $"event-photo:{row.Id}",
                    row.EventId.ToString(),
                    MediaAssetOrigin.SocialMediaEventPhoto,
                    MediaAssetKind.Photo,
                    Path.GetFileName(row.StorageKey),
                    row.ContentType,
                    $"event:{row.EventId}",
                    $"event:{row.EventId}",
                    row.EventTitle,
                    string.IsNullOrWhiteSpace(row.EventType) ? "Social media event" : row.EventType,
                    "Event",
                    string.IsNullOrWhiteSpace(row.Caption) ? row.EventTitle : row.Caption,
                    row.Caption,
                    null,
                    date,
                    row.Width,
                    row.Height,
                    null,
                    row.VersionStamp,
                    row.IsCover,
                    row.CreatedAtUtc.UtcDateTime.Ticks,
                    row.VersionStamp));
            }

            foreach (var stale in existing.Values.Where(asset => asset.LastSeenScanId != scanId))
            {
                stale.IsAvailable = false;
                stale.LastSeenAtUtc = now;
            }

            await _mediaDb.SaveChangesAsync(cancellationToken);

            await EnsurePhotoProcessingJobsAsync(source.Id, now, cancellationToken);

            source.IndexedAssetCount = await _mediaDb.Assets.CountAsync(
                asset => asset.SourceId == source.Id && asset.IsAvailable && !asset.IsDeleted,
                cancellationToken);
            source.LastScanCompletedAtUtc = DateTimeOffset.UtcNow;
            source.LastSuccessfulScanAtUtc = source.LastScanCompletedAtUtc;
            source.ScanStatus = "Healthy";
            source.LastError = null;
            source.ScanRequestedAtUtc = null;
            await _mediaDb.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            source.LastScanCompletedAtUtc = DateTimeOffset.UtcNow;
            source.ScanStatus = "Failed";
            source.LastError = Trim(ex.GetBaseException().Message, 2048);
            await _mediaDb.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "PRISM media catalogue synchronization failed");
            throw;
        }
    }

    private MediaAsset Upsert(
        IDictionary<string, MediaAsset> existing,
        Guid sourceId,
        Guid scanId,
        DateTimeOffset now,
        AssetValues values)
    {
        if (!existing.TryGetValue(values.SourceEntityId, out var asset))
        {
            asset = new MediaAsset
            {
                SourceId = sourceId,
                SourceEntityId = values.SourceEntityId,
                IndexedAtUtc = now,
                DerivativeStatus = values.Kind == MediaAssetKind.Photo
                    ? MediaProcessingStatus.Pending
                    : MediaProcessingStatus.Unsupported,
                AnalysisStatus = values.Kind == MediaAssetKind.Photo
                    ? MediaProcessingStatus.Pending
                    : MediaProcessingStatus.NotRequested,
                Classification = values.Kind == MediaAssetKind.Photo
                    ? MediaClassification.Photograph
                    : MediaClassification.Unknown
            };
            existing.Add(values.SourceEntityId, asset);
            _mediaDb.Assets.Add(asset);
        }

        var contentChanged = !string.Equals(asset.QuickFingerprint, values.Fingerprint, StringComparison.Ordinal);
        if (contentChanged && asset.Id != 0 && values.Kind == MediaAssetKind.Photo)
        {
            asset.CacheVersion++;
            asset.ContentHash = null;
            asset.DerivativeStatus = MediaProcessingStatus.Pending;
            asset.AnalysisStatus = _options.Classification.Enabled
                ? MediaProcessingStatus.Pending
                : MediaProcessingStatus.NotRequested;
            asset.ClassificationConfidence = null;
            asset.AnalysisSignalsJson = null;
            asset.AnalysedAtUtc = null;
            asset.ProcessingFailureReason = null;
        }

        asset.ParentEntityId = values.ParentEntityId;
        asset.Origin = values.Origin;
        asset.Kind = values.Kind;
        asset.OriginalFileName = values.OriginalFileName;
        asset.ContentType = values.ContentType;
        asset.ContextKey = values.ContextKey;
        asset.CollectionKey = values.CollectionKey;
        asset.ContextTitle = values.ContextTitle;
        asset.ContextSubtitle = values.ContextSubtitle;
        asset.SourceLabel = values.SourceLabel;
        asset.Title = values.Title;
        asset.Caption = values.Caption;
        asset.ProjectId = values.ProjectId;
        asset.MediaDateUtc = values.MediaDateUtc;
        asset.Width = values.Width;
        asset.Height = values.Height;
        asset.DurationSeconds = values.DurationSeconds;
        asset.VersionToken = values.VersionToken;
        asset.IsCover = values.IsCover;
        asset.SortOrder = values.SortOrder;
        asset.QuickFingerprint = values.Fingerprint;
        // A catalogue row whose physical content was conclusively unavailable stays
        // hidden until its source fingerprint changes. This prevents reconciliation
        // from reviving and requeueing the same broken historical record on every scan.
        var preserveUnavailable = !contentChanged
            && MediaProcessingFailurePolicy.HasSourceUnavailableMarker(asset.ProcessingFailureReason);
        asset.IsAvailable = !preserveUnavailable;
        asset.AvailabilityStatus = preserveUnavailable
            ? (asset.AvailabilityStatus == MediaAvailabilityStatus.Available
                ? MediaAvailabilityStatus.SourceMissing
                : asset.AvailabilityStatus)
            : MediaAvailabilityStatus.Available;
        if (!preserveUnavailable)
        {
            asset.UnavailableReason = null;
            asset.UnavailableSinceUtc = null;
            asset.LastAvailabilityCheckUtc = DateTimeOffset.UtcNow;
        }
        asset.IsDeleted = false;
        asset.LastSeenAtUtc = now;
        asset.LastSeenScanId = scanId;
        return asset;
    }

    private async Task EnsurePhotoProcessingJobsAsync(
        Guid sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await _mediaDb.Assets
            .Where(asset => asset.SourceId == sourceId
                            && asset.Kind == MediaAssetKind.Photo
                            && asset.IsAvailable
                            && !asset.IsDeleted
                            && (asset.DerivativeStatus == MediaProcessingStatus.Pending
                                || (_options.Classification.Enabled
                                    && (asset.AnalysisStatus == MediaProcessingStatus.NotRequested
                                        || asset.AnalysisStatus == MediaProcessingStatus.Pending))))
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return;

        var existingJobs = await _mediaDb.ProcessingJobs
            .Where(job => candidates.Contains(job.MediaAssetId)
                          && job.JobType == MediaProcessingJobType.AnalyseAsset)
            .ToDictionaryAsync(job => job.MediaAssetId, cancellationToken);

        foreach (var assetId in candidates)
        {
            if (existingJobs.TryGetValue(assetId, out var existingJob))
            {
                // Completed jobs are only requeued when the asset state itself was reset
                // to Pending because its source fingerprint changed. Permanent failures are
                // never revived by routine reconciliation.
                if (existingJob.Status == MediaProcessingJobStatus.Completed)
                {
                    existingJob.Status = MediaProcessingJobStatus.Pending;
                    existingJob.AttemptCount = 0;
                    existingJob.AvailableAfterUtc = now;
                    existingJob.StartedAtUtc = null;
                    existingJob.CompletedAtUtc = null;
                    existingJob.LockedBy = null;
                    existingJob.LockExpiresAtUtc = null;
                    existingJob.FailureCode = null;
                    existingJob.FailureMessage = null;
                    existingJob.UpdatedAtUtc = now;
                }
                else if (existingJob.Status is MediaProcessingJobStatus.Failed or MediaProcessingJobStatus.DeadLetter
                         && MediaProcessingFailurePolicy.IsRecoverableFailureCode(existingJob.FailureCode))
                {
                    existingJob.Status = MediaProcessingJobStatus.Pending;
                    existingJob.AttemptCount = 0;
                    existingJob.AvailableAfterUtc = now;
                    existingJob.StartedAtUtc = null;
                    existingJob.CompletedAtUtc = null;
                    existingJob.LockedBy = null;
                    existingJob.LockExpiresAtUtc = null;
                    existingJob.FailureCode = null;
                    existingJob.FailureMessage = null;
                    existingJob.UpdatedAtUtc = now;
                }
                continue;
            }

            _mediaDb.ProcessingJobs.Add(new MediaProcessingJob
            {
                MediaAssetId = assetId,
                JobType = MediaProcessingJobType.AnalyseAsset,
                Status = MediaProcessingJobStatus.Pending,
                AvailableAfterUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                MaxAttempts = 5
            });
        }

        await _mediaDb.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset ToUtcOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record AssetValues(
        string SourceEntityId,
        string ParentEntityId,
        MediaAssetOrigin Origin,
        MediaAssetKind Kind,
        string OriginalFileName,
        string ContentType,
        string ContextKey,
        string CollectionKey,
        string ContextTitle,
        string ContextSubtitle,
        string SourceLabel,
        string Title,
        string? Caption,
        int? ProjectId,
        DateTimeOffset MediaDateUtc,
        int? Width,
        int? Height,
        int? DurationSeconds,
        string? VersionToken,
        bool IsCover,
        long SortOrder,
        string Fingerprint);
}
