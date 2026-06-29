using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Maintains review-only person suggestions for unassigned faces. Suggestions are disposable
/// model output: they may be refreshed or removed, but they never create an identity assignment.
/// Human rejections are retained and respected by the candidate search service.
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
        var face = await _db.Faces
            .AsNoTracking()
            .Include(item => item.Embeddings.Where(embedding => embedding.InvalidatedAtUtc == null))
            .SingleOrDefaultAsync(item => item.Id == faceId
                                          && !item.IsSuppressed
                                          && item.QualityStatus == FaceQualityStatus.EmbeddingEligible,
                cancellationToken);
        if (face is null)
        {
            return 0;
        }

        var hasAssignment = await _db.PersonFaces
            .AsNoTracking()
            .AnyAsync(assignment => assignment.MediaFaceId == faceId
                                    && assignment.RemovedAtUtc == null,
                cancellationToken);
        var intentionallyUnidentified = await _db.FaceReviewDecisions
            .AsNoTracking()
            .AnyAsync(decision => decision.MediaFaceId == faceId
                                  && !decision.CandidatePersonId.HasValue
                                  && decision.Decision == FaceReviewDecisionType.Ignored,
                cancellationToken);
        if (hasAssignment || intentionallyUnidentified)
        {
            return 0;
        }

        var embedding = face.Embeddings
            .Where(item => item.ModelKey == _options.Embedder.Key
                           && item.ModelVersion == _options.Embedder.Version
                           && item.Dimension == _options.Embedder.EmbeddingDimension)
            .OrderByDescending(item => item.QualityScore)
            .ThenByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault();
        if (embedding is null || embedding.Embedding.Length == 0)
        {
            return 0;
        }

        var candidates = await _candidateSearch.SearchAsync(
            faceId,
            embedding.Embedding,
            embedding.ModelKey,
            embedding.ModelVersion,
            embedding.Dimension,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var pending = await _db.FaceReviewDecisions
            .Where(decision => decision.MediaFaceId == faceId
                               && decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Pending)
            .ToListAsync(cancellationToken);

        // Suggestions from a superseded embedding model must not remain actionable.
        foreach (var decision in pending.Where(decision =>
                     decision.ModelKey != embedding.ModelKey
                     || decision.ModelVersion != embedding.ModelVersion))
        {
            decision.Decision = FaceReviewDecisionType.Rejected;
            decision.DecidedAtUtc = now;
            decision.Notes = "Suggestion superseded by the current face-embedding model.";
            decision.ConcurrencyToken = Guid.NewGuid();
        }

        var currentPending = pending
            .Where(decision => decision.ModelKey == embedding.ModelKey
                               && decision.ModelVersion == embedding.ModelVersion)
            .ToDictionary(decision => decision.CandidatePersonId!.Value);
        var candidateIds = candidates.Select(candidate => candidate.PersonId).ToHashSet();
        foreach (var stale in currentPending.Values.Where(decision =>
                     !candidateIds.Contains(decision.CandidatePersonId!.Value)))
        {
            _db.FaceReviewDecisions.Remove(stale);
        }

        foreach (var candidate in candidates)
        {
            if (currentPending.TryGetValue(candidate.PersonId, out var existing))
            {
                existing.Similarity = candidate.Similarity;
                existing.Notes = BuildEvidenceNote(candidate);
                existing.ConcurrencyToken = Guid.NewGuid();
                continue;
            }

            _db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
            {
                MediaFaceId = faceId,
                CandidatePersonId = candidate.PersonId,
                Decision = FaceReviewDecisionType.Pending,
                Similarity = candidate.Similarity,
                ModelKey = embedding.ModelKey,
                ModelVersion = embedding.ModelVersion,
                Notes = BuildEvidenceNote(candidate),
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAtUtc = now
            });
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return candidates.Count;
    }

    public async Task<int> RefreshUnassignedAsync(int limit, CancellationToken cancellationToken)
    {
        var maximum = Math.Clamp(limit, 1, 10_000);
        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var dimension = _options.Embedder.EmbeddingDimension;
        var faceIds = await _db.Faces
            .AsNoTracking()
            .Where(face => !face.IsSuppressed
                           && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)
                           && face.Embeddings.Any(embedding =>
                               embedding.InvalidatedAtUtc == null
                               && embedding.ModelKey == modelKey
                               && embedding.ModelVersion == modelVersion
                               && embedding.Dimension == dimension)
                           && !_db.FaceReviewDecisions.Any(decision =>
                               decision.MediaFaceId == face.Id
                               && !decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Ignored))
            .OrderByDescending(face => face.MediaAsset.MediaDateUtc)
            .ThenByDescending(face => face.QualityScore)
            .Select(face => face.Id)
            .Take(maximum)
            .ToListAsync(cancellationToken);

        var refreshed = 0;
        foreach (var faceId in faceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RefreshFaceAsync(faceId, cancellationToken);
                refreshed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to refresh identity candidates for face {FaceId}; remaining faces will continue.",
                    faceId);
            }
        }

        return refreshed;
    }

    private static string BuildEvidenceNote(FaceCandidate candidate)
        => $"Aggregate {candidate.Similarity:0.000}; best reference {candidate.BestReferenceSimilarity:0.000}; "
           + $"top-reference mean {candidate.MeanTopSimilarity:0.000}; references {candidate.ReferenceCount}.";
}
