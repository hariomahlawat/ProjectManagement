using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Bounded, model-compatible candidate search. Only high-quality, human-confirmed
/// references are considered. A candidate is scored from both its best reference and
/// the repeatability of its top references so one accidental close vector cannot dominate.
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

        // Keep provider-side work relational and scalar. The former navigation SelectMany
        // projected into a CLR record before ordering, which Npgsql could not translate.
        var databaseRows = await BuildReferenceRowsQuery(
                _db,
                faceId,
                modelKey,
                modelVersion,
                dimension,
                maximumReferences)
            .ToListAsync(cancellationToken);

        var references = databaseRows
            .Where(reference => reference.Embedding is { Length: > 0 }
                                && reference.Embedding.Length == dimension)
            .GroupBy(reference => reference.FaceId)
            .Select(group => group
                .OrderByDescending(reference => reference.QualityScore)
                .ThenByDescending(reference => reference.EmbeddingId)
                .First())
            .Select(reference => new ReferenceRow(
                reference.PersonId,
                reference.DisplayName,
                reference.RepresentativeFaceId,
                reference.Embedding,
                reference.QualityScore,
                reference.AssignedAtUtc))
            .ToList();
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
            .GroupBy(reference => new { reference.PersonId, reference.DisplayName, reference.RepresentativeFaceId })
            .Where(group => !rejected.Contains(group.Key.PersonId))
            .Select(group =>
            {
                var score = FaceSimilarityScoring.ScoreReferences(
                    embedding,
                    group
                        .OrderByDescending(reference => reference.QualityScore)
                        .ThenByDescending(reference => reference.AssignedAtUtc)
                        .Select(reference => (IReadOnlyList<float>)reference.Embedding),
                    Math.Clamp(_options.ReferenceFacesPerPerson, 1, 50));
                return new FaceCandidate(
                    group.Key.PersonId,
                    group.Key.DisplayName,
                    group.Key.RepresentativeFaceId,
                    score.AggregateSimilarity,
                    score.BestSimilarity,
                    score.MeanTopSimilarity,
                    score.ReferenceCount);
            })
            .Where(candidate => candidate.Similarity >= _options.CandidateSimilarityThreshold)
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenByDescending(candidate => candidate.ReferenceCount)
            .ThenBy(candidate => candidate.DisplayName)
            .Take(Math.Clamp(_options.CandidateLimit, 1, 20))
            .ToList();
    }

    /// <summary>
    /// Builds a PostgreSQL-translatable query for active, model-compatible reference faces.
    /// Only references whose source media remains available are included.
    /// </summary>
    internal static IQueryable<CandidateReferenceDatabaseRow> BuildReferenceRowsQuery(
        MediaLibraryDbContext db,
        Guid excludedFaceId,
        string modelKey,
        string modelVersion,
        int dimension,
        int maximumReferences)
    {
        ArgumentNullException.ThrowIfNull(db);

        var ordered =
            from assignment in db.PersonFaces.AsNoTracking()
            join person in db.Persons.AsNoTracking()
                on assignment.MediaPersonId equals person.Id
            join face in db.Faces.AsNoTracking()
                on assignment.MediaFaceId equals face.Id
            join asset in db.Assets.AsNoTracking()
                on face.MediaAssetId equals asset.Id
            join reference in db.FaceEmbeddings.AsNoTracking()
                on face.Id equals reference.MediaFaceId
            where assignment.RemovedAtUtc == null
                  && assignment.MediaFaceId != excludedFaceId
                  && !person.IsHidden
                  && person.Status == MediaPersonStatus.Confirmed
                  && !face.IsSuppressed
                  && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                  && asset.IsAvailable
                  && !asset.IsDeleted
                  && !asset.IsArchived
                  && (assignment.AssignmentType == FaceAssignmentType.HumanConfirmed
                      || assignment.AssignmentType == FaceAssignmentType.ManualAssignment)
                  && reference.InvalidatedAtUtc == null
                  && reference.ModelKey == modelKey
                  && reference.ModelVersion == modelVersion
                  && reference.Dimension == dimension
            orderby reference.QualityScore descending,
                assignment.AssignedAtUtc descending,
                reference.Id descending
            select new
            {
                FaceId = face.Id,
                EmbeddingId = reference.Id,
                PersonId = assignment.MediaPersonId,
                person.DisplayName,
                person.RepresentativeFaceId,
                reference.Embedding,
                QualityScore = reference.QualityScore,
                assignment.AssignedAtUtc
            };

        return ordered
            .Take(Math.Clamp(maximumReferences, 1, 250_000))
            .Select(row => new CandidateReferenceDatabaseRow
            {
                FaceId = row.FaceId,
                EmbeddingId = row.EmbeddingId,
                PersonId = row.PersonId,
                DisplayName = row.DisplayName,
                RepresentativeFaceId = row.RepresentativeFaceId,
                Embedding = row.Embedding,
                QualityScore = row.QualityScore,
                AssignedAtUtc = row.AssignedAtUtc
            });
    }

    public static double CosineSimilarity(float[] first, float[] second)
        => FaceSimilarityScoring.CosineSimilarity(first, second);

    private sealed record ReferenceRow(
        Guid PersonId,
        string DisplayName,
        Guid? RepresentativeFaceId,
        float[] Embedding,
        double QualityScore,
        DateTimeOffset AssignedAtUtc);
}

/// <summary>
/// Flat database projection used by candidate search. Similarity calculations remain in
/// memory because PostgreSQL is not asked to interpret the raw SFace vector semantics.
/// </summary>
internal sealed class CandidateReferenceDatabaseRow
{
    public Guid FaceId { get; init; }
    public long EmbeddingId { get; init; }
    public Guid PersonId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public Guid? RepresentativeFaceId { get; init; }
    public float[] Embedding { get; init; } = Array.Empty<float>();
    public double QualityScore { get; init; }
    public DateTimeOffset AssignedAtUtc { get; init; }
}
