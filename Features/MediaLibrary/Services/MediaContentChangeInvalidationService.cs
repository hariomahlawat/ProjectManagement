using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Captures and retires all intelligence derived from an earlier version of an image.
/// Source records remain authoritative; a changed binary must never inherit classification,
/// face detections, embeddings or human identity assignments from the previous content.
/// </summary>
public interface IMediaContentChangeInvalidationService
{
    MediaContentChangeSnapshot ResetAsset(
        MediaAsset asset,
        string newFingerprint,
        MediaAssetKind newKind,
        bool classificationEnabled);

    Task RetireDerivedIntelligenceAsync(
        IReadOnlyCollection<MediaContentChangeSnapshot> changes,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken);

    Task RetireFaceIntelligenceAsync(
        IReadOnlyCollection<long> assetIds,
        string action,
        string performedByUserId,
        string notes,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken);
}

public sealed record MediaContentChangeSnapshot(
    long AssetId,
    string? PreviousFingerprint,
    string NewFingerprint,
    MediaClassification PreviousClassification,
    bool PreviousWasManual,
    MediaClassificationDecisionStatus PreviousDecisionStatus,
    MediaClassification PreviousPredictedClassification,
    decimal PreviousPredictedScore);

public sealed class MediaContentChangeInvalidationService : IMediaContentChangeInvalidationService
{
    private const string SystemActor = "system:media-catalogue";
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaCachePathResolver _cache;
    private readonly ILogger<MediaContentChangeInvalidationService> _logger;

    public MediaContentChangeInvalidationService(
        MediaLibraryDbContext db,
        IMediaCachePathResolver cache,
        ILogger<MediaContentChangeInvalidationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MediaContentChangeSnapshot ResetAsset(
        MediaAsset asset,
        string newFingerprint,
        MediaAssetKind newKind,
        bool classificationEnabled)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFingerprint);
        if (asset.Id == 0)
        {
            throw new InvalidOperationException("Only persisted media assets can be invalidated.");
        }

        var snapshot = new MediaContentChangeSnapshot(
            asset.Id,
            asset.QuickFingerprint,
            newFingerprint,
            asset.Classification,
            asset.ClassificationIsManual,
            asset.ClassificationDecisionStatus,
            asset.PredictedClassification,
            asset.PredictedClassificationScore);

        asset.CacheVersion++;
        asset.ContentHash = null;
        asset.DerivativeStatus = newKind == MediaAssetKind.Photo
            ? MediaProcessingStatus.Pending
            : MediaProcessingStatus.Unsupported;
        asset.AnalysisStatus = newKind == MediaAssetKind.Photo && classificationEnabled
            ? MediaProcessingStatus.Pending
            : MediaProcessingStatus.NotRequested;
        asset.PredictedClassification = MediaClassification.Unknown;
        asset.PredictedClassificationScore = 0m;
        asset.Classification = MediaClassification.Unknown;
        asset.ClassificationConfidence = null;
        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.NotProcessed;
        asset.ClassificationDecisionReasonCode = "SOURCE_CONTENT_CHANGED";
        asset.ClassificationIsManual = false;
        asset.ClassificationUpdatedByUserId = null;
        asset.ClassificationReviewedByUserId = null;
        asset.ClassificationReviewedAt = null;
        asset.ClassificationReviewReason = null;
        asset.ClassificationConcurrencyToken = Guid.NewGuid();
        asset.AutomaticClassificationSignalsJson = null;
        asset.AutomaticClassificationScoresJson = null;
        asset.AutomaticClassificationMetricsJson = null;
        asset.AnalysisVersion = null;
        asset.ClassifierVersion = null;
        asset.AnalysisSignalsJson = null;
        asset.AnalysedAtUtc = null;
        asset.ClassifiedAtUtc = null;
        asset.FaceAnalysisStatus = MediaProcessingStatus.NotRequested;
        asset.FaceAnalysisVersion = null;
        asset.FaceAnalysedAtUtc = null;
        asset.FaceProcessingFailureReason = null;
        asset.ProcessingFailureReason = null;
        return snapshot;
    }

    public async Task RetireDerivedIntelligenceAsync(
        IReadOnlyCollection<MediaContentChangeSnapshot> changes,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return;
        }

        var byAssetId = changes
            .GroupBy(change => change.AssetId)
            .ToDictionary(group => group.Key, group => group.Last());
        var assetIds = byAssetId.Keys.ToArray();
        var faces = await _db.Faces
            .Include(face => face.PersonAssignments)
            .Where(face => assetIds.Contains(face.MediaAssetId))
            .ToListAsync(cancellationToken);

        foreach (var face in faces)
        {
            var change = byAssetId[face.MediaAssetId];
            var activeAssignments = face.PersonAssignments
                .Where(assignment => assignment.RemovedAtUtc == null)
                .ToArray();
            if (activeAssignments.Length == 0)
            {
                _db.IdentityAudits.Add(CreateIdentityAudit(
                    face,
                    change,
                    null,
                    "Detected face invalidated because the source image bytes changed.",
                    changedAtUtc));
                continue;
            }

            foreach (var assignment in activeAssignments)
            {
                _db.IdentityAudits.Add(CreateIdentityAudit(
                    face,
                    change,
                    assignment,
                    "Human-reviewed identity retired because the source image bytes changed.",
                    changedAtUtc));
            }
        }

        if (faces.Count > 0)
        {
            // Embeddings, assignments and review decisions are cascade-deleted. Identity audit
            // rows are deliberately independent so the historical decision remains traceable.
            _db.Faces.RemoveRange(faces);
            DeleteFaceThumbnails(faces.Select(face => face.ReviewThumbnailPath));
        }

        foreach (var change in byAssetId.Values)
        {
            _db.ClassificationAudits.Add(new MediaClassificationAudit
            {
                MediaAssetId = change.AssetId,
                PreviousClassification = change.PreviousClassification,
                NewClassification = MediaClassification.Unknown,
                PreviousWasManual = change.PreviousWasManual,
                NewIsManual = false,
                AutomaticPredictedClassification = change.PreviousPredictedClassification,
                AutomaticPredictedScore = change.PreviousPredictedScore,
                PreviousDecisionStatus = change.PreviousDecisionStatus,
                NewDecisionStatus = MediaClassificationDecisionStatus.NotProcessed,
                CorrelationId = Trim($"source-change:{change.NewFingerprint}", 128),
                ChangedByUserId = SystemActor,
                Reason = "Classification invalidated because the source image bytes changed.",
                ChangedAtUtc = changedAtUtc
            });
        }
    }


    public async Task RetireFaceIntelligenceAsync(
        IReadOnlyCollection<long> assetIds,
        string action,
        string performedByUserId,
        string notes,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(performedByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notes);
        if (action.Length > 64)
        {
            throw new ArgumentException("The identity-audit action must not exceed 64 characters.", nameof(action));
        }

        var uniqueAssetIds = assetIds.Distinct().ToArray();
        if (uniqueAssetIds.Length == 0)
        {
            return;
        }

        var faces = await _db.Faces
            .Include(face => face.PersonAssignments)
            .Where(face => uniqueAssetIds.Contains(face.MediaAssetId))
            .ToListAsync(cancellationToken);
        foreach (var face in faces)
        {
            var activeAssignments = face.PersonAssignments
                .Where(assignment => assignment.RemovedAtUtc == null)
                .ToArray();
            if (activeAssignments.Length == 0)
            {
                _db.IdentityAudits.Add(CreateRetirementAudit(
                    face,
                    null,
                    action,
                    performedByUserId,
                    notes,
                    changedAtUtc));
                continue;
            }

            foreach (var assignment in activeAssignments)
            {
                _db.IdentityAudits.Add(CreateRetirementAudit(
                    face,
                    assignment,
                    action,
                    performedByUserId,
                    notes,
                    changedAtUtc));
            }
        }

        if (faces.Count == 0)
        {
            return;
        }

        _db.Faces.RemoveRange(faces);
        DeleteFaceThumbnails(faces.Select(face => face.ReviewThumbnailPath));
    }

    private static MediaIdentityAudit CreateIdentityAudit(
        MediaFace face,
        MediaContentChangeSnapshot change,
        MediaPersonFace? assignment,
        string notes,
        DateTimeOffset changedAtUtc)
        => new()
        {
            FaceId = face.Id,
            PersonId = assignment?.MediaPersonId,
            PreviousPersonId = assignment?.MediaPersonId,
            Action = "SourceContentReplaced",
            PerformedByUserId = SystemActor,
            Notes = notes,
            MetadataJson = JsonSerializer.Serialize(new
            {
                face.MediaAssetId,
                change.PreviousFingerprint,
                change.NewFingerprint,
                AssignmentType = assignment?.AssignmentType,
                AssignedAtUtc = assignment?.AssignedAtUtc
            }),
            PerformedAtUtc = changedAtUtc
        };

    private static MediaIdentityAudit CreateRetirementAudit(
        MediaFace face,
        MediaPersonFace? assignment,
        string action,
        string performedByUserId,
        string notes,
        DateTimeOffset changedAtUtc)
        => new()
        {
            FaceId = face.Id,
            PersonId = assignment?.MediaPersonId,
            PreviousPersonId = assignment?.MediaPersonId,
            Action = action,
            PerformedByUserId = performedByUserId,
            Notes = Trim(notes, 1024),
            MetadataJson = JsonSerializer.Serialize(new
            {
                face.MediaAssetId,
                AssignmentType = assignment?.AssignmentType,
                AssignedAtUtc = assignment?.AssignedAtUtc
            }),
            PerformedAtUtc = changedAtUtc
        };

    private void DeleteFaceThumbnails(IEnumerable<string?> relativePaths)
    {
        var root = Path.GetFullPath(_cache.CacheRoot);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var relativePath in relativePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(
                    root,
                    relativePath!.Replace('/', Path.DirectorySeparatorChar)));
                if (!candidate.StartsWith(rootPrefix, comparison))
                {
                    _logger.LogWarning(
                        "Skipped unsafe face-thumbnail path while retiring media intelligence: {ThumbnailPath}",
                        relativePath);
                    continue;
                }

                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or ArgumentException
                                               or System.Security.SecurityException)
            {
                // Database retirement remains authoritative. An inaccessible orphan is not
                // exposed because its face row is removed; operations can purge it later.
                _logger.LogWarning(
                    exception,
                    "Unable to delete retired face thumbnail {ThumbnailPath}.",
                    relativePath);
            }
        }
    }

    private static string Trim(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}
