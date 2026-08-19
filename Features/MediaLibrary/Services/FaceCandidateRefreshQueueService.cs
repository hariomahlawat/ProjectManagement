using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Durable candidate-search queue implemented on MediaFace state. It avoids request-time
/// similarity scans and remains safe across application restarts without introducing a
/// second generic job table.
/// </summary>
public sealed class FaceCandidateRefreshQueueService : IFaceCandidateRefreshQueueService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IFaceCandidateRefreshRuntimeState _runtime;
    private readonly MediaPeopleOptions _options;

    public FaceCandidateRefreshQueueService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IFaceCandidateRefreshRuntimeState runtime,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<bool> QueueFaceAsync(Guid faceId, CancellationToken cancellationToken)
    {
        if (!IsOperational())
        {
            return false;
        }

        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var face = await BuildQueueableFacesQuery(
                _db,
                _options.Embedder.Key,
                _options.Embedder.Version,
                _options.Embedder.EmbeddingDimension,
                _options.CandidateMinimumFaceQuality,
                visibleAssetIds)
            .SingleOrDefaultAsync(item => item.Id == faceId, cancellationToken);
        if (face is null)
        {
            return false;
        }

        MarkPending(face);
        await _db.SaveChangesAsync(cancellationToken);
        _runtime.RequestRun();
        return true;
    }

    public async Task<int> QueueFacesAsync(
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(faceIds);
        if (!IsOperational())
        {
            return 0;
        }

        var selected = faceIds
            .Where(faceId => faceId != Guid.Empty)
            .Distinct()
            .Take(500)
            .ToArray();
        if (selected.Length == 0)
        {
            return 0;
        }

        var queued = await QueueQueryAsync(
            query => query.Where(face => selected.Contains(face.Id)),
            cancellationToken);
        if (queued > 0)
        {
            _runtime.RequestRun();
        }
        return queued;
    }

    public async Task<int> QueueAllUnassignedAsync(CancellationToken cancellationToken)
    {
        if (!IsOperational())
        {
            return 0;
        }

        var queued = await QueueQueryAsync(query => query, cancellationToken);
        if (queued > 0)
        {
            _runtime.RequestRun();
        }
        return queued;
    }

    public async Task<int> RecoverStaleProcessingAsync(CancellationToken cancellationToken)
    {
        if (!IsOperational())
        {
            return 0;
        }

        var staleBeforeUtc = DateTimeOffset.UtcNow.AddSeconds(
            -Math.Clamp(_options.CandidateProcessingStaleSeconds, 30, 3600));
        var now = DateTimeOffset.UtcNow;
        var visibleAssetIds = BuildVisibleAssetIdsQuery();

        var recovered = await BuildQueueableFacesQuery(
                _db,
                _options.Embedder.Key,
                _options.Embedder.Version,
                _options.Embedder.EmbeddingDimension,
                _options.CandidateMinimumFaceQuality,
                visibleAssetIds)
            .Where(face => face.CandidateSearchStatus == FaceCandidateSearchStatus.Processing
                           && face.UpdatedAtUtc < staleBeforeUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(face => face.CandidateSearchStatus, FaceCandidateSearchStatus.Pending)
                .SetProperty(face => face.CandidateSearchFailureReason, (string?)null)
                .SetProperty(face => face.CandidateSearchCompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(face => face.UpdatedAtUtc, now),
                cancellationToken);

        if (recovered > 0)
        {
            _runtime.MarkRecovered(recovered);
            _runtime.RequestRun();
        }
        return recovered;
    }

    internal static IQueryable<MediaFace> BuildQueueableFacesQuery(
        MediaLibraryDbContext db,
        string modelKey,
        string modelVersion,
        int dimension,
        double minimumFaceQuality,
        IQueryable<long>? visibleAssetIds = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);

        var query = db.Faces
            .Where(face => !face.IsSuppressed
                           && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                           && face.QualityScore >= minimumFaceQuality
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)
                           && face.Embeddings.Any(embedding =>
                               embedding.InvalidatedAtUtc == null
                               && embedding.ModelKey == modelKey
                               && embedding.ModelVersion == modelVersion
                               && embedding.Dimension == dimension)
                           && !db.FaceReviewDecisions.Any(decision =>
                               decision.MediaFaceId == face.Id
                               && !decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Ignored));

        return visibleAssetIds is null
            ? query
            : query.Where(face => visibleAssetIds.Contains(face.MediaAssetId));
    }

    private async Task<int> QueueQueryAsync(
        Func<IQueryable<MediaFace>, IQueryable<MediaFace>> shape,
        CancellationToken cancellationToken)
    {
        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var now = DateTimeOffset.UtcNow;
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var query = BuildQueueableFacesQuery(
            _db,
            modelKey,
            modelVersion,
            _options.Embedder.EmbeddingDimension,
            _options.CandidateMinimumFaceQuality,
            visibleAssetIds);

        return await shape(query)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(face => face.CandidateSearchStatus, FaceCandidateSearchStatus.Pending)
                .SetProperty(face => face.CandidateSearchModelKey, modelKey)
                .SetProperty(face => face.CandidateSearchModelVersion, modelVersion)
                .SetProperty(face => face.CandidateSearchFailureReason, (string?)null)
                .SetProperty(face => face.CandidateSearchCompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(face => face.UpdatedAtUtc, now),
                cancellationToken);
    }

    private IQueryable<long> BuildVisibleAssetIdsQuery()
        => _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

    private bool IsOperational()
        => _options.Enabled && _options.WorkerEnabled && _options.CandidateSearchEnabled;

    private void MarkPending(MediaFace face)
    {
        face.CandidateSearchStatus = FaceCandidateSearchStatus.Pending;
        face.CandidateSearchModelKey = _options.Embedder.Key;
        face.CandidateSearchModelVersion = _options.Embedder.Version;
        face.CandidateSearchFailureReason = null;
        face.CandidateSearchCompletedAtUtc = null;
        face.UpdatedAtUtc = DateTimeOffset.UtcNow;
        face.ConcurrencyToken = Guid.NewGuid();
    }
}
