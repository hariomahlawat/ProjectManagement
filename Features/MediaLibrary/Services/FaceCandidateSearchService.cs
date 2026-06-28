using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Bounded, model-compatible candidate search. It deliberately uses only a limited set
/// of high-quality, human-confirmed reference faces per person. The abstraction can be
/// replaced by a pgvector-backed implementation without changing the processing pipeline.
/// </summary>
public sealed class FaceCandidateSearchService : IFaceCandidateSearchService
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaPeopleOptions _options;

    public FaceCandidateSearchService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<FaceCandidate>> SearchAsync(
        Guid faceId,
        float[] embedding,
        string modelKey,
        string modelVersion,
        int dimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length != dimension || embedding.Length == 0)
        {
            return Array.Empty<FaceCandidate>();
        }

        var maximumReferences = Math.Clamp(
            _options.MaximumCandidateReferenceEmbeddings,
            100,
            250_000);
        var references = await _db.PersonFaces
            .AsNoTracking()
            .Where(assignment => assignment.RemovedAtUtc == null
                                 && assignment.MediaFaceId != faceId
                                 && !assignment.MediaPerson.IsHidden
                                 && assignment.MediaPerson.Status == MediaPersonStatus.Confirmed
                                 && !assignment.MediaFace.IsSuppressed
                                 && (assignment.AssignmentType == FaceAssignmentType.HumanConfirmed
                                     || assignment.AssignmentType == FaceAssignmentType.ManualAssignment))
            .SelectMany(
                assignment => assignment.MediaFace.Embeddings
                    .Where(reference => reference.InvalidatedAtUtc == null
                                        && reference.ModelKey == modelKey
                                        && reference.ModelVersion == modelVersion
                                        && reference.Dimension == dimension),
                (assignment, reference) => new ReferenceRow(
                    assignment.MediaPersonId,
                    assignment.MediaPerson.DisplayName,
                    reference.Embedding,
                    reference.QualityScore,
                    assignment.AssignedAtUtc))
            .OrderByDescending(reference => reference.QualityScore)
            .ThenByDescending(reference => reference.AssignedAtUtc)
            .Take(maximumReferences)
            .ToListAsync(cancellationToken);

        if (references.Count == 0)
        {
            return Array.Empty<FaceCandidate>();
        }

        var rejectedPersonIds = await _db.FaceReviewDecisions
            .AsNoTracking()
            .Where(decision => decision.MediaFaceId == faceId
                               && decision.CandidatePersonId.HasValue
                               && decision.ModelKey == modelKey
                               && decision.ModelVersion == modelVersion
                               && decision.Decision == FaceReviewDecisionType.Rejected)
            .Select(decision => decision.CandidatePersonId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var rejected = rejectedPersonIds.ToHashSet();

        return references
            .GroupBy(reference => new { reference.PersonId, reference.DisplayName })
            .Where(group => !rejected.Contains(group.Key.PersonId))
            .Select(group =>
            {
                var similarity = group
                    .Take(Math.Clamp(_options.ReferenceFacesPerPerson, 1, 50))
                    .Select(reference => CosineSimilarity(embedding, reference.Embedding))
                    .DefaultIfEmpty(-1)
                    .Max();
                return new FaceCandidate(group.Key.PersonId, group.Key.DisplayName, similarity);
            })
            .Where(candidate => candidate.Similarity >= _options.CandidateSimilarityThreshold)
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.DisplayName)
            .Take(Math.Clamp(_options.CandidateLimit, 1, 20))
            .ToList();
    }

    public static double CosineSimilarity(float[] first, float[] second)
    {
        if (first.Length == 0 || first.Length != second.Length)
        {
            return -1;
        }

        double dot = 0;
        double firstNorm = 0;
        double secondNorm = 0;
        for (var index = 0; index < first.Length; index++)
        {
            dot += first[index] * second[index];
            firstNorm += first[index] * first[index];
            secondNorm += second[index] * second[index];
        }

        return firstNorm <= 1e-12 || secondNorm <= 1e-12
            ? -1
            : dot / Math.Sqrt(firstNorm * secondNorm);
    }

    private sealed record ReferenceRow(
        Guid PersonId,
        string DisplayName,
        float[] Embedding,
        double QualityScore,
        DateTimeOffset AssignedAtUtc);
}
