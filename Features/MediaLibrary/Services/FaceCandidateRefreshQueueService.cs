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
    private readonly MediaPeopleOptions _options;

    public FaceCandidateRefreshQueueService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<bool> QueueFaceAsync(Guid faceId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.WorkerEnabled || !_options.CandidateSearchEnabled)
        {
            return false;
        }

        var face = await BuildQueueableFacesQuery(
                _db,
                _options.Embedder.Key,
                _options.Embedder.Version,
                _options.Embedder.EmbeddingDimension,
                _options.CandidateMinimumFaceQuality)
            .SingleOrDefaultAsync(item => item.Id == faceId, cancellationToken);
        if (face is null)
        {
            return false;
        }

        MarkPending(face);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> QueueAllUnassignedAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.WorkerEnabled || !_options.CandidateSearchEnabled)
        {
            return 0;
        }

        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var now = DateTimeOffset.UtcNow;
        return await BuildQueueableFacesQuery(
                _db,
                modelKey,
                modelVersion,
                _options.Embedder.EmbeddingDimension,
                _options.CandidateMinimumFaceQuality)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(face => face.CandidateSearchStatus, FaceCandidateSearchStatus.Pending)
                .SetProperty(face => face.CandidateSearchModelKey, modelKey)
                .SetProperty(face => face.CandidateSearchModelVersion, modelVersion)
                .SetProperty(face => face.CandidateSearchFailureReason, (string?)null)
                .SetProperty(face => face.CandidateSearchCompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(face => face.UpdatedAtUtc, now),
                cancellationToken);
    }

    internal static IQueryable<MediaFace> BuildQueueableFacesQuery(
        MediaLibraryDbContext db,
        string modelKey,
        string modelVersion,
        int dimension,
        double minimumFaceQuality)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);

        return db.Faces
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
    }

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
