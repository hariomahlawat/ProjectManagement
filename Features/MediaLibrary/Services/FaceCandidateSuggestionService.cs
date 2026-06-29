using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Maintains review-only person suggestions for unassigned faces. Candidate lookup is
/// batched so confirmed reference embeddings are loaded once per cycle. Suggestions are
/// disposable model output and never create an identity assignment automatically.
/// </summary>
public sealed class FaceCandidateSuggestionService : IFaceCandidateSuggestionService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IFaceCandidateSearchService _candidateSearch;
    private readonly MediaPeopleOptions _options;
    private readonly ILogger<FaceCandidateSuggestionService> _logger;

    public FaceCandidateSuggestionService(
        MediaLibraryDbContext db,
        IFaceCandidateSearchService candidateSearch,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceCandidateSuggestionService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _candidateSearch = candidateSearch ?? throw new ArgumentNullException(nameof(candidateSearch));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> RefreshFaceAsync(Guid faceId, CancellationToken cancellationToken)
    {
        if (!_options.CandidateSearchEnabled)
        {
            return 0;
        }

        var face = await BuildCandidateFaceQuery()
            .Where(item => item.Id == faceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (face is null)
        {
            return 0;
        }

        face.CandidateSearchStatus = FaceCandidateSearchStatus.Processing;
        face.CandidateSearchModelKey = _options.Embedder.Key;
        face.CandidateSearchModelVersion = _options.Embedder.Version;
        face.CandidateSearchFailureReason = null;
        face.CandidateSearchCompletedAtUtc = null;
        face.UpdatedAtUtc = DateTimeOffset.UtcNow;
        face.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);

        return await RefreshFacesCoreAsync(new[] { face }, cancellationToken);
    }

    public async Task<int> RefreshUnassignedAsync(int limit, CancellationToken cancellationToken)
    {
        if (!_options.CandidateSearchEnabled)
        {
            return 0;
        }

        var maximum = Math.Clamp(limit, 1, 10_000);
        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var retryBeforeUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var faces = await BuildCandidateFaceQuery()
            .Where(face => face.CandidateSearchStatus == FaceCandidateSearchStatus.Pending
                           || face.CandidateSearchStatus == FaceCandidateSearchStatus.NotRequested
                           || face.CandidateSearchModelKey != modelKey
                           || face.CandidateSearchModelVersion != modelVersion
                           || (face.CandidateSearchStatus == FaceCandidateSearchStatus.Failed
                               && (!face.CandidateSearchCompletedAtUtc.HasValue
                                   || face.CandidateSearchCompletedAtUtc < retryBeforeUtc))
                           || (face.CandidateSearchStatus == FaceCandidateSearchStatus.Processing
                               && face.UpdatedAtUtc < retryBeforeUtc))
            .OrderBy(face => face.CandidateSearchStatus == FaceCandidateSearchStatus.Pending ? 0 : 1)
            .ThenByDescending(face => face.MediaAsset.MediaDateUtc)
            .ThenByDescending(face => face.QualityScore)
            .Take(maximum)
            .ToListAsync(cancellationToken);
        if (faces.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var face in faces)
        {
            face.CandidateSearchStatus = FaceCandidateSearchStatus.Processing;
            face.CandidateSearchModelKey = modelKey;
            face.CandidateSearchModelVersion = modelVersion;
            face.CandidateSearchFailureReason = null;
            face.CandidateSearchCompletedAtUtc = null;
            face.UpdatedAtUtc = now;
            face.ConcurrencyToken = Guid.NewGuid();
        }
        await _db.SaveChangesAsync(cancellationToken);

        return await RefreshFacesCoreAsync(faces, cancellationToken);
    }

    private IQueryable<MediaFace> BuildCandidateFaceQuery()
    {
        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var dimension = _options.Embedder.EmbeddingDimension;
        return FaceCandidateRefreshQueueService.BuildQueueableFacesQuery(
                _db,
                modelKey,
                modelVersion,
                dimension,
                _options.CandidateMinimumFaceQuality)
            .Include(face => face.Embeddings.Where(embedding =>
                embedding.InvalidatedAtUtc == null
                && embedding.ModelKey == modelKey
                && embedding.ModelVersion == modelVersion
                && embedding.Dimension == dimension));
    }

    private async Task<int> RefreshFacesCoreAsync(
        IReadOnlyCollection<MediaFace> faces,
        CancellationToken cancellationToken)
    {
        if (faces.Count == 0)
        {
            return 0;
        }

        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var dimension = _options.Embedder.EmbeddingDimension;
        var inputs = faces
            .Select(face => new
            {
                Face = face,
                Embedding = face.Embeddings
                    .Where(item => item.ModelKey == modelKey
                                   && item.ModelVersion == modelVersion
                                   && item.Dimension == dimension
                                   && item.InvalidatedAtUtc == null)
                    .OrderByDescending(item => item.QualityScore)
                    .ThenByDescending(item => item.CreatedAtUtc)
                    .FirstOrDefault()
            })
            .Where(item => item.Embedding is not null && item.Embedding.Embedding.Length > 0)
            .Select(item => new FaceCandidateSearchInput(
                item.Face.Id,
                item.Face.MediaAssetId,
                item.Embedding!.Embedding,
                item.Embedding.ModelKey,
                item.Embedding.ModelVersion,
                item.Embedding.Dimension))
            .ToList();

        var inputFaceIds = inputs.Select(input => input.FaceId).ToHashSet();
        var facesWithoutUsableEmbedding = faces
            .Where(face => !inputFaceIds.Contains(face.Id))
            .ToList();
        if (facesWithoutUsableEmbedding.Count > 0)
        {
            var completedAt = DateTimeOffset.UtcNow;
            foreach (var face in facesWithoutUsableEmbedding)
            {
                face.CandidateSearchStatus = FaceCandidateSearchStatus.NotRequested;
                face.CandidateSearchFailureReason = "No current, valid face embedding is available.";
                face.CandidateSearchCompletedAtUtc = completedAt;
                face.UpdatedAtUtc = completedAt;
                face.ConcurrencyToken = Guid.NewGuid();
            }
        }

        if (inputs.Count == 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return 0;
        }

        IReadOnlyDictionary<Guid, IReadOnlyList<FaceCandidate>> candidatesByFace;
        try
        {
            candidatesByFace = await _candidateSearch.SearchBatchAsync(inputs, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = Trim(exception.GetBaseException().Message, 2048);
            foreach (var face in faces)
            {
                face.CandidateSearchStatus = FaceCandidateSearchStatus.Failed;
                face.CandidateSearchFailureReason = message;
                face.CandidateSearchCompletedAtUtc = DateTimeOffset.UtcNow;
                face.UpdatedAtUtc = DateTimeOffset.UtcNow;
                face.ConcurrencyToken = Guid.NewGuid();
            }
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var faceIds = inputs.Select(input => input.FaceId).ToArray();
        var pending = await _db.FaceReviewDecisions
            .Where(decision => faceIds.Contains(decision.MediaFaceId)
                               && decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Pending)
            .ToListAsync(cancellationToken);
        var pendingByFace = pending
            .GroupBy(decision => decision.MediaFaceId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var completedAt = DateTimeOffset.UtcNow;

        foreach (var input in inputs)
        {
            var face = faces.Single(item => item.Id == input.FaceId);
            var candidates = candidatesByFace.GetValueOrDefault(input.FaceId)
                             ?? Array.Empty<FaceCandidate>();
            var facePending = pendingByFace.GetValueOrDefault(input.FaceId)
                              ?? new List<MediaFaceReviewDecision>();

            foreach (var decision in facePending.Where(decision =>
                         decision.ModelKey != input.ModelKey
                         || decision.ModelVersion != input.ModelVersion))
            {
                decision.Decision = FaceReviewDecisionType.Rejected;
                decision.DecidedAtUtc = completedAt;
                decision.Notes = "Suggestion superseded by the current face-embedding model.";
                decision.ConcurrencyToken = Guid.NewGuid();
            }

            var currentPending = facePending
                .Where(decision => decision.ModelKey == input.ModelKey
                                   && decision.ModelVersion == input.ModelVersion)
                .ToDictionary(decision => decision.CandidatePersonId!.Value);
            var candidateIds = candidates.Select(candidate => candidate.PersonId).ToHashSet();
            foreach (var stale in currentPending.Values.Where(decision =>
                         !candidateIds.Contains(decision.CandidatePersonId!.Value)))
            {
                _db.FaceReviewDecisions.Remove(stale);
            }

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var nextSimilarity = index + 1 < candidates.Count
                    ? candidates[index + 1].Similarity
                    : (double?)null;
                var note = BuildEvidenceNote(candidate, nextSimilarity);
                if (currentPending.TryGetValue(candidate.PersonId, out var existing))
                {
                    existing.Similarity = candidate.Similarity;
                    existing.Notes = note;
                    existing.ConcurrencyToken = Guid.NewGuid();
                    continue;
                }

                _db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
                {
                    MediaFaceId = input.FaceId,
                    CandidatePersonId = candidate.PersonId,
                    Decision = FaceReviewDecisionType.Pending,
                    Similarity = candidate.Similarity,
                    ModelKey = input.ModelKey,
                    ModelVersion = input.ModelVersion,
                    Notes = note,
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedAtUtc = completedAt
                });
            }

            face.CandidateSearchStatus = FaceCandidateSearchStatus.Ready;
            face.CandidateSearchModelKey = input.ModelKey;
            face.CandidateSearchModelVersion = input.ModelVersion;
            face.CandidateSearchFailureReason = null;
            face.CandidateSearchCompletedAtUtc = completedAt;
            face.UpdatedAtUtc = completedAt;
            face.ConcurrencyToken = Guid.NewGuid();
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Refreshed known-person candidates for {FaceCount} face(s) using one bounded reference-set load.",
            inputs.Count);
        return inputs.Count;
    }

    private string BuildEvidenceNote(FaceCandidate candidate, double? nextSimilarity)
    {
        var margin = nextSimilarity.HasValue
            ? candidate.Similarity - nextSimilarity.Value
            : candidate.Similarity;
        var strength = candidate.Similarity >= _options.CandidateStrongSimilarityThreshold
                       && margin >= _options.CandidateMinimumMargin
            ? "strong"
            : "review";
        return $"Aggregate {candidate.Similarity:0.000}; best reference {candidate.BestReferenceSimilarity:0.000}; "
               + $"top-reference mean {candidate.MeanTopSimilarity:0.000}; references {candidate.ReferenceCount}; "
               + $"margin {margin:0.000}; classification {strength}.";
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
