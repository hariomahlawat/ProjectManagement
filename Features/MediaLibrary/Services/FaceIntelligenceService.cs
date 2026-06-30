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
    private readonly MediaLibraryOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FaceIntelligenceService> _logger;

    public FaceIntelligenceService(
        MediaLibraryDbContext db,
        IMediaContentProviderResolver resolver,
        IFaceAnalysisEngine engine,
        IFaceEligibilityPolicy eligibility,
        IMediaContentChangeInvalidationService contentInvalidation,
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment,
        ILogger<FaceIntelligenceService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _contentInvalidation = contentInvalidation ?? throw new ArgumentNullException(nameof(contentInvalidation));
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

            _db.Faces.RemoveRange(trackedExisting);
            var sequence = 0;
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
            }

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
