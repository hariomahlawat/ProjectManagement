using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class FaceIntelligenceService : IFaceIntelligenceService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaContentProviderResolver _resolver;
    private readonly IFaceAnalysisEngine _engine;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly IMediaContentChangeInvalidationService _contentInvalidation;
    private readonly IFaceReviewInvalidationCoordinator _reviewInvalidation;
    private readonly MediaLibraryOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FaceIntelligenceService> _logger;

    public FaceIntelligenceService(
        MediaLibraryDbContext db,
        IMediaContentProviderResolver resolver,
        IFaceAnalysisEngine engine,
        IFaceEligibilityPolicy eligibility,
        IMediaContentChangeInvalidationService contentInvalidation,
        IFaceReviewInvalidationCoordinator reviewInvalidation,
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment,
        ILogger<FaceIntelligenceService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _contentInvalidation = contentInvalidation ?? throw new ArgumentNullException(nameof(contentInvalidation));
        _reviewInvalidation = reviewInvalidation ?? throw new ArgumentNullException(nameof(reviewInvalidation));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessAssetAsync(long assetId, CancellationToken cancellationToken)
    {
        if (!_options.People.Enabled)
        {
            return;
        }

        var asset = await _db.Assets
            .Include(item => item.Source)
            .SingleAsync(item => item.Id == assetId, cancellationToken);
        if (!asset.IsAvailable || asset.IsDeleted)
        {
            throw new MediaProcessingSupersededException(
                $"Media asset {assetId} is no longer available for face analysis.");
        }

        var processingCacheVersion = asset.CacheVersion;
        var content = await _resolver.ResolveAsync(asset, cancellationToken)
            ?? throw new MediaContentUnavailableException(
                $"Media content is unavailable for face analysis of asset {asset.Id}.");
        var bytes = await ReadBoundedAsync(
            await content.OpenReadAsync(cancellationToken),
            _options.Processing.MaxImageFileSizeBytes,
            cancellationToken);
        var actualContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var existingFaces = await _db.Faces
            .AsNoTracking()
            .Include(face => face.PersonAssignments)
            .Where(face => face.MediaAssetId == assetId)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(asset.ContentHash)
            && !string.Equals(asset.ContentHash, actualContentHash, StringComparison.OrdinalIgnoreCase))
        {
            var contentChangedAtUtc = DateTimeOffset.UtcNow;
            var change = _contentInvalidation.ResetAsset(
                asset,
                $"sha256:{actualContentHash}",
                asset.Kind,
                _options.Classification.Enabled);
            asset.ContentHash = actualContentHash;
            await _contentInvalidation.RetireDerivedIntelligenceAsync(
                new[] { change },
                contentChangedAtUtc,
                cancellationToken);
            await QueueAnalyseAssetAsync(asset.Id, contentChangedAtUtc, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Retired stale face intelligence for asset {AssetId} after an exact content-hash change; classification was requeued.",
                asset.Id);
            return;
        }

        var contentHashWasMissing = string.IsNullOrWhiteSpace(asset.ContentHash);
        asset.ContentHash ??= actualContentHash;
        var eligibility = _eligibility.Evaluate(asset);
        if (!eligibility.IsEligible)
        {
            _logger.LogInformation(
                "Skipping face analysis for asset {AssetId}: {EligibilityCode} - {EligibilityReason}",
                asset.Id,
                eligibility.Code,
                eligibility.Reason);
            if (contentHashWasMissing)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        if (existingFaces.Any(face => face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)))
        {
            _logger.LogInformation(
                "Skipping face reprocessing for asset {AssetId} because it contains a human-reviewed assignment.",
                assetId);
            if (contentHashWasMissing)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        asset.FaceAnalysisStatus = MediaProcessingStatus.Processing;
        asset.FaceProcessingFailureReason = null;
        await _db.SaveChangesAsync(cancellationToken);

        var detections = await _engine.AnalyseAsync(bytes, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var oldThumbnailPaths = existingFaces
            .Select(face => face.ReviewThumbnailPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();

        var newThumbnailPaths = new List<string>();
        var committed = false;
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await _db.Entry(asset).ReloadAsync(cancellationToken);
            var currentEligibility = _eligibility.Evaluate(asset);
            if (!asset.IsAvailable
                || asset.IsDeleted
                || asset.CacheVersion != processingCacheVersion
                || !currentEligibility.IsEligible
                || !string.Equals(asset.ContentHash, actualContentHash, StringComparison.OrdinalIgnoreCase))
            {
                _db.ChangeTracker.Clear();
                throw new MediaProcessingSupersededException(
                    $"Face analysis for media asset {assetId} was superseded by source removal, content replacement or eligibility change.");
            }

            var trackedExisting = await _db.Faces
                .Include(face => face.Embeddings)
                .Include(face => face.PersonAssignments)
                .Where(face => face.MediaAssetId == assetId)
                .ToListAsync(cancellationToken);
            if (trackedExisting.Any(face => face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)))
            {
                asset.FaceAnalysisStatus = MediaProcessingStatus.Ready;
                asset.FaceProcessingFailureReason = null;
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var sequence = 0;
            var createdFaces = new List<(MediaFace Face, DetectedFaceData Detection)>();
            foreach (var detection in detections)
            {
                sequence++;
                var faceId = Guid.NewGuid();
                var thumbnailPath = await SaveThumbnailAsync(
                    faceId,
                    detection.ReviewThumbnail,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(thumbnailPath))
                {
                    newThumbnailPaths.Add(thumbnailPath);
                }

                var face = new MediaFace
                {
                    Id = faceId,
                    MediaAssetId = assetId,
                    SequenceNumber = sequence,
                    Left = detection.Left,
                    Top = detection.Top,
                    Width = detection.Width,
                    Height = detection.Height,
                    LandmarksJson = detection.Landmarks is null
                        ? null
                        : JsonSerializer.Serialize(detection.Landmarks),
                    DetectionConfidence = detection.Confidence,
                    QualityScore = detection.QualityScore,
                    QualityStatus = detection.QualityStatus,
                    BlurScore = detection.BlurScore,
                    BrightnessScore = detection.BrightnessScore,
                    PoseScore = detection.PoseScore,
                    QualitySignalsJson = detection.QualitySignals is null
                        ? null
                        : JsonSerializer.Serialize(detection.QualitySignals),
                    DetectorModelKey = _options.People.Detector.Key,
                    DetectorModelVersion = _options.People.Detector.Version,
                    ReviewThumbnailPath = thumbnailPath,
                    CandidateSearchStatus = detection.Embedding is { Length: > 0 }
                        ? FaceCandidateSearchStatus.Pending
                        : FaceCandidateSearchStatus.NotRequested,
                    CandidateSearchModelKey = detection.Embedding is { Length: > 0 }
                        ? _options.People.Embedder.Key
                        : null,
                    CandidateSearchModelVersion = detection.Embedding is { Length: > 0 }
                        ? _options.People.Embedder.Version
                        : null,
                    CandidateSearchFailureReason = null,
                    CandidateSearchCompletedAtUtc = null,
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                if (detection.Embedding is { Length: > 0 })
                {
                    face.Embeddings.Add(new MediaFaceEmbedding
                    {
                        Embedding = detection.Embedding,
                        Dimension = detection.Embedding.Length,
                        ModelKey = _options.People.Embedder.Key,
                        ModelVersion = _options.People.Embedder.Version,
                        Normalization = "L2",
                        QualityScore = detection.QualityScore,
                        CreatedAtUtc = now
                    });
                }

                _db.Faces.Add(face);
                createdFaces.Add((face, detection));
            }

            // Carry human triage over before removing the old detections: deleting a face
            // cascades to its review decisions and assignment history, and EF applies that
            // cascade to tracked dependents as soon as the face is marked deleted.
            var triageMoved = await CarryOverHumanTriageAsync(trackedExisting, createdFaces, now, cancellationToken);
            if (triageMoved)
            {
                // (MediaAssetId, SequenceNumber) is unique. Inserting a new face, moving
                // decisions onto it and deleting the old face that holds the same sequence
                // number in one save is a dependency cycle EF cannot order. Insert the new
                // faces on temporary negative sequence numbers first, then delete the old
                // faces and assign the final numbers. Both saves share this transaction.
                foreach (var (face, _) in createdFaces)
                {
                    face.SequenceNumber = -face.SequenceNumber;
                }

                await _db.SaveChangesAsync(cancellationToken);

                foreach (var (face, _) in createdFaces)
                {
                    face.SequenceNumber = -face.SequenceNumber;
                }
            }

            _db.Faces.RemoveRange(trackedExisting);

            asset.FaceAnalysisStatus = MediaProcessingStatus.Ready;
            asset.FaceAnalysisVersion = CurrentAnalysisVersion;
            asset.FaceAnalysedAtUtc = now;
            asset.FaceProcessingFailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                DeleteThumbnails(newThumbnailPaths, "uncommitted");
            }
        }

        DeleteThumbnails(oldThumbnailPaths, "obsolete");

        // CandidateSearchStatus is persisted with each new embedding. The dedicated
        // candidate worker will perform bounded matching outside this processing transaction.
    }

    public async Task<int> RefreshEmbeddingsAsync(
        long assetId,
        CancellationToken cancellationToken)
    {
        if (!_options.People.Enabled)
        {
            return 0;
        }

        var asset = await _db.Assets
            .Include(item => item.Source)
            .Include(item => item.Faces)
                .ThenInclude(face => face.Embeddings)
            .Include(item => item.Faces)
                .ThenInclude(face => face.PersonAssignments)
            .SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
            ?? throw new MediaProcessingSupersededException(
                $"Media asset {assetId} no longer exists for face-embedding preparation.");

        if (!asset.IsAvailable || asset.IsDeleted || asset.IsArchived)
        {
            throw new MediaProcessingSupersededException(
                $"Media asset {assetId} is no longer available for face-embedding preparation.");
        }
        if (asset.Faces.Count == 0)
        {
            throw new InvalidOperationException(
                "No existing face detections are available to prepare for matching.");
        }

        var eligibility = _eligibility.Evaluate(asset);
        if (!eligibility.IsEligible)
        {
            throw new InvalidOperationException(
                $"Face-embedding preparation is not permitted for this photograph: {eligibility.Reason}");
        }

        var processingCacheVersion = asset.CacheVersion;
        var originalHash = asset.ContentHash;
        var content = await _resolver.ResolveAsync(asset, cancellationToken)
            ?? throw new MediaContentUnavailableException(
                $"Media content is unavailable for face-embedding preparation of asset {asset.Id}.");
        var bytes = await ReadBoundedAsync(
            await content.OpenReadAsync(cancellationToken),
            _options.Processing.MaxImageFileSizeBytes,
            cancellationToken);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(originalHash)
            && !string.Equals(originalHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new MediaProcessingSupersededException(
                "The source photograph changed after its confirmed face was reviewed. Review the photograph before rebuilding biometric evidence.");
        }

        asset.ContentHash ??= actualHash;
        asset.FaceAnalysisStatus = MediaProcessingStatus.Processing;
        asset.FaceProcessingFailureReason = null;
        await _db.SaveChangesAsync(cancellationToken);

        IReadOnlyList<DetectedFaceData> detections;
        try
        {
            detections = await _engine.AnalyseAsync(bytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = exception.GetBaseException().Message;
            asset.FaceAnalysisStatus = MediaProcessingStatus.Failed;
            asset.FaceProcessingFailureReason = failure.Length <= 2048 ? failure : failure[..2048];
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var usableDetections = detections
            .Select((detection, index) => new { Detection = detection, Index = index })
            .ToList();
        var usedDetectionIndexes = new HashSet<int>();
        var now = DateTimeOffset.UtcNow;
        var refreshed = 0;
        var trustedEvidenceChanged = false;

        foreach (var face in asset.Faces.OrderBy(item => item.SequenceNumber))
        {
            var best = usableDetections
                .Where(item => !usedDetectionIndexes.Contains(item.Index))
                .Select(item => new
                {
                    item.Detection,
                    item.Index,
                    Overlap = IntersectionOverUnion(face, item.Detection)
                })
                .OrderByDescending(item => item.Overlap)
                .FirstOrDefault();

            if (best is null || best.Overlap < 0.25d)
            {
                continue;
            }

            usedDetectionIndexes.Add(best.Index);
            var detection = best.Detection;
            face.DetectionConfidence = detection.Confidence;
            face.QualityScore = detection.QualityScore;
            face.QualityStatus = detection.QualityStatus;
            face.BlurScore = detection.BlurScore;
            face.BrightnessScore = detection.BrightnessScore;
            face.PoseScore = detection.PoseScore;
            face.QualitySignalsJson = detection.QualitySignals is null
                ? null
                : JsonSerializer.Serialize(detection.QualitySignals);
            face.DetectorModelKey = _options.People.Detector.Key;
            face.DetectorModelVersion = _options.People.Detector.Version;
            face.UpdatedAtUtc = now;
            face.ConcurrencyToken = Guid.NewGuid();

            var activeEmbeddings = face.Embeddings
                .Where(item => item.InvalidatedAtUtc == null)
                .ToList();
            var hasTrustedAssignment = face.PersonAssignments.Any(assignment =>
                assignment.RemovedAtUtc == null
                && assignment.ReferenceStatus == FaceReferenceStatus.TrustedReference);

            if (detection.Embedding is { Length: > 0 }
                && detection.Embedding.Length == _options.People.Embedder.EmbeddingDimension
                && FaceQualityEvaluator.CanGenerateEmbedding(detection.QualityStatus))
            {
                foreach (var existing in activeEmbeddings)
                {
                    existing.InvalidatedAtUtc = now;
                }

                face.Embeddings.Add(new MediaFaceEmbedding
                {
                    MediaFaceId = face.Id,
                    Embedding = detection.Embedding,
                    Dimension = detection.Embedding.Length,
                    ModelKey = _options.People.Embedder.Key,
                    ModelVersion = _options.People.Embedder.Version,
                    Normalization = "L2",
                    QualityScore = detection.QualityScore,
                    CreatedAtUtc = now
                });
                refreshed++;
                trustedEvidenceChanged |= hasTrustedAssignment;

                if (!face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null))
                {
                    face.CandidateSearchStatus = FaceCandidateSearchStatus.Pending;
                    face.CandidateSearchModelKey = _options.People.Embedder.Key;
                    face.CandidateSearchModelVersion = _options.People.Embedder.Version;
                    face.CandidateSearchFailureReason = null;
                    face.CandidateSearchCompletedAtUtc = null;
                }
            }
        }

        await _db.Entry(asset).ReloadAsync(cancellationToken);
        if (!asset.IsAvailable
            || asset.IsDeleted
            || asset.IsArchived
            || asset.CacheVersion != processingCacheVersion)
        {
            _db.ChangeTracker.Clear();
            throw new MediaProcessingSupersededException(
                $"Face-embedding preparation for media asset {assetId} was superseded by a source or catalogue change.");
        }

        if (refreshed == 0)
        {
            asset.FaceAnalysisStatus = MediaProcessingStatus.Failed;
            asset.FaceProcessingFailureReason =
                "No existing confirmed face could be matched to a current technically usable face embedding. Choose another clear appearance or review the detection.";
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(asset.FaceProcessingFailureReason);
        }

        asset.FaceAnalysisStatus = MediaProcessingStatus.Ready;
        asset.FaceAnalysisVersion = CurrentAnalysisVersion;
        asset.FaceAnalysedAtUtc = now;
        asset.FaceProcessingFailureReason = null;
        await _db.SaveChangesAsync(cancellationToken);

        if (trustedEvidenceChanged)
        {
            await _reviewInvalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Prepared {FaceCount} current face embedding(s) for media asset {AssetId} without replacing human-reviewed face assignments.",
            refreshed,
            assetId);
        return refreshed;
    }

    /// <summary>
    /// Minimum overlap for treating a new detection as the same face as an old one when
    /// carrying human triage across re-analysis. Stricter than embedding refresh because
    /// a wrong match would attach someone else's review decisions to a face.
    /// </summary>
    private const double TriageCarryOverMinimumOverlap = 0.5d;

    /// <summary>
    /// Re-analysis (for example after a detector or embedder upgrade) replaces every face
    /// on a photograph without a confirmed person. Human triage on those faces, such as
    /// "not a face", "leave unidentified", rejected suggestions and removed-assignment
    /// history, used to be deleted with them, and the same work returned to the review
    /// queue. Each old face that carries triage is matched one-to-one to the new
    /// detection it overlaps most, and the triage moves to that face. Machine-generated
    /// pending suggestions are not carried; candidate search recomputes them.
    /// </summary>
    private async Task<bool> CarryOverHumanTriageAsync(
        IReadOnlyList<MediaFace> oldFaces,
        IReadOnlyList<(MediaFace Face, DetectedFaceData Detection)> newFaces,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (oldFaces.Count == 0 || newFaces.Count == 0)
        {
            return false;
        }

        var oldFaceIds = oldFaces.Select(face => face.Id).ToArray();
        var humanDecisions = await _db.FaceReviewDecisions
            .Where(decision => oldFaceIds.Contains(decision.MediaFaceId)
                               && decision.Decision != FaceReviewDecisionType.Pending)
            .ToListAsync(cancellationToken);
        var decisionsByFace = humanDecisions
            .GroupBy(decision => decision.MediaFaceId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var triaged = oldFaces
            .Where(face => face.IsSuppressed
                           || decisionsByFace.ContainsKey(face.Id)
                           || face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc != null))
            .ToList();
        if (triaged.Count == 0)
        {
            return false;
        }

        var pairs = triaged
            .SelectMany(oldFace => newFaces.Select(created => (
                Old: oldFace,
                New: created.Face,
                Overlap: IntersectionOverUnion(oldFace, created.Detection))))
            .Where(pair => pair.Overlap >= TriageCarryOverMinimumOverlap)
            .OrderByDescending(pair => pair.Overlap)
            .ToList();

        var matchedOld = new HashSet<Guid>();
        var matchedNew = new HashSet<Guid>();
        var carried = 0;
        foreach (var (oldFace, newFace, _) in pairs)
        {
            if (!matchedOld.Add(oldFace.Id))
            {
                continue;
            }

            if (!matchedNew.Add(newFace.Id))
            {
                matchedOld.Remove(oldFace.Id);
                continue;
            }

            if (oldFace.IsSuppressed)
            {
                newFace.IsSuppressed = true;
                newFace.QualityStatus = FaceQualityStatus.Suppressed;
                newFace.SuppressedAtUtc = oldFace.SuppressedAtUtc;
                newFace.SuppressedByUserId = oldFace.SuppressedByUserId;
                newFace.CandidateSearchStatus = FaceCandidateSearchStatus.NotRequested;
                newFace.CandidateSearchFailureReason = null;
                newFace.CandidateSearchCompletedAtUtc = now;
                foreach (var embedding in newFace.Embeddings)
                {
                    embedding.InvalidatedAtUtc = now;
                }
            }

            if (decisionsByFace.TryGetValue(oldFace.Id, out var decisions))
            {
                foreach (var decision in decisions)
                {
                    decision.MediaFace = newFace;
                    decision.MediaFaceId = newFace.Id;
                    decision.ConcurrencyToken = Guid.NewGuid();
                }

                if (!newFace.IsSuppressed
                    && decisions.Any(decision => decision.Decision == FaceReviewDecisionType.Ignored
                                                 && decision.CandidatePersonId == null))
                {
                    // "Leave unidentified" closes the face for review; keep it closed.
                    newFace.CandidateSearchStatus = FaceCandidateSearchStatus.Ready;
                    newFace.CandidateSearchFailureReason = null;
                    newFace.CandidateSearchCompletedAtUtc = now;
                }
            }

            foreach (var assignment in oldFace.PersonAssignments
                         .Where(assignment => assignment.RemovedAtUtc != null)
                         .ToList())
            {
                oldFace.PersonAssignments.Remove(assignment);
                assignment.MediaFace = newFace;
                assignment.MediaFaceId = newFace.Id;
                newFace.PersonAssignments.Add(assignment);
            }

            carried++;
        }

        // Make the moved foreign keys visible before the old faces are marked deleted, so
        // the cascade does not reach the decisions and history that now belong elsewhere.
        _db.ChangeTracker.DetectChanges();

        if (carried < triaged.Count)
        {
            _logger.LogInformation(
                "Re-analysis carried human triage for {Carried} of {Triaged} face(s) on media asset {AssetId}; unmatched faces were no longer detected.",
                carried,
                triaged.Count,
                oldFaces[0].MediaAssetId);
        }

        return carried > 0;
    }

    private static double IntersectionOverUnion(MediaFace face, DetectedFaceData detection)
    {
        var left = Math.Max(face.Left, detection.Left);
        var top = Math.Max(face.Top, detection.Top);
        var right = Math.Min(face.Left + face.Width, detection.Left + detection.Width);
        var bottom = Math.Min(face.Top + face.Height, detection.Top + detection.Height);
        var intersection = Math.Max(0d, right - left) * Math.Max(0d, bottom - top);
        if (intersection <= 0d)
        {
            return 0d;
        }

        var faceArea = Math.Max(0d, face.Width) * Math.Max(0d, face.Height);
        var detectionArea = Math.Max(0d, detection.Width) * Math.Max(0d, detection.Height);
        var union = faceArea + detectionArea - intersection;
        return union <= 0d ? 0d : intersection / union;
    }

    private string CurrentAnalysisVersion
        => $"{_options.People.Detector.Key}:{_options.People.Detector.Version}|{_options.People.Embedder.Key}:{_options.People.Embedder.Version}";

    private async Task QueueAnalyseAssetAsync(
        long assetId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await _db.ProcessingJobs.SingleOrDefaultAsync(
            item => item.MediaAssetId == assetId
                    && item.JobType == MediaProcessingJobType.AnalyseAsset,
            cancellationToken);
        if (job is null)
        {
            _db.ProcessingJobs.Add(new MediaProcessingJob
            {
                MediaAssetId = assetId,
                JobType = MediaProcessingJobType.AnalyseAsset,
                Status = MediaProcessingJobStatus.Pending,
                AvailableAfterUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                MaxAttempts = 5
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
        job.AvailableAfterUtc = now;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
        job.LockedBy = null;
        job.LockExpiresAtUtc = null;
        job.FailureCode = null;
        job.FailureMessage = null;
        job.UpdatedAtUtc = now;
    }

    private async Task<string?> SaveThumbnailAsync(
        Guid faceId,
        byte[]? bytes,
        CancellationToken cancellationToken)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var root = ResolveCacheRoot();
        var fileName = faceId.ToString("N") + ".webp";
        var relative = Path.Combine("faces", fileName[..2], fileName);
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        EnsureInsideRoot(root, fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private void DeleteThumbnails(IEnumerable<string> relativePaths, string reason)
    {
        var root = ResolveCacheRoot();
        foreach (var relativePath in relativePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(
                    root,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                EnsureInsideRoot(root, fullPath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or ArgumentException)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to remove {Reason} face thumbnail {ThumbnailPath}.",
                    reason,
                    relativePath);
            }
        }
    }

    private string ResolveCacheRoot()
        => Path.GetFullPath(Path.IsPathRooted(_options.CacheRoot)
            ? _options.CacheRoot
            : Path.Combine(_environment.ContentRootPath, _options.CacheRoot));

    private static void EnsureInsideRoot(string root, string candidate)
    {
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException("Resolved media-cache path escapes the configured cache root.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using (stream)
        {
            using var memory = new MemoryStream();
            var buffer = new byte[81_920];
            long total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maximumBytes)
                {
                    throw new InvalidDataException(
                        $"Face analysis input exceeds the configured limit of {maximumBytes} bytes.");
                }

                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return memory.ToArray();
        }
    }
}
