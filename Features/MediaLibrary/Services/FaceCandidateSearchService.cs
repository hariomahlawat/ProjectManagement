using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Bounded, model-compatible candidate search. Batch searches load the confirmed reference
/// set once, then score multiple new faces in memory. Similarity is review evidence only and
/// never confirms an identity automatically.
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

        var assetId = await _db.Faces
            .AsNoTracking()
            .Where(face => face.Id == faceId)
            .Select(face => (long?)face.MediaAssetId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!assetId.HasValue)
        {
            return Array.Empty<FaceCandidate>();
        }

        var result = await SearchBatchAsync(
            new[]
            {
                new FaceCandidateSearchInput(
                    faceId,
                    assetId.Value,
                    embedding,
                    modelKey,
                    modelVersion,
                    dimension)
            },
            cancellationToken);

        return result.GetValueOrDefault(faceId) ?? Array.Empty<FaceCandidate>();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<FaceCandidate>>> SearchBatchAsync(
        IReadOnlyCollection<FaceCandidateSearchInput> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var validInputs = inputs
            .Where(input => input.FaceId != Guid.Empty
                            && input.AssetId > 0
                            && input.Dimension > 0
                            && input.Embedding is { Length: > 0 }
                            && input.Embedding.Length == input.Dimension
                            && !string.IsNullOrWhiteSpace(input.ModelKey)
                            && !string.IsNullOrWhiteSpace(input.ModelVersion))
            .GroupBy(input => input.FaceId)
            .Select(group => group.First())
            .ToList();
        if (validInputs.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<FaceCandidate>>();
        }

        var results = validInputs.ToDictionary(
            input => input.FaceId,
            _ => (IReadOnlyList<FaceCandidate>)Array.Empty<FaceCandidate>());

        foreach (var modelGroup in validInputs.GroupBy(input => new
                 {
                     input.ModelKey,
                     input.ModelVersion,
                     input.Dimension
                 }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupInputs = modelGroup.ToList();
            var faceIds = groupInputs.Select(input => input.FaceId).ToArray();
            var assetIds = groupInputs.Select(input => input.AssetId).Distinct().ToArray();
            var maximumReferences = Math.Clamp(
                _options.MaximumCandidateReferenceEmbeddings,
                100,
                250_000);

            var databaseRows = await BuildReferenceRowsQuery(
                    _db,
                    Guid.Empty,
                    modelGroup.Key.ModelKey,
                    modelGroup.Key.ModelVersion,
                    modelGroup.Key.Dimension,
                    maximumReferences)
                .ToListAsync(cancellationToken);

            var references = databaseRows
                .Where(reference => reference.Embedding is { Length: > 0 }
                                    && reference.Embedding.Length == modelGroup.Key.Dimension)
                .GroupBy(reference => reference.FaceId)
                .Select(group => group
                    .OrderByDescending(reference => reference.QualityScore)
                    .ThenByDescending(reference => reference.EmbeddingId)
                    .First())
                .GroupBy(reference => new
                {
                    reference.PersonId,
                    reference.DisplayName,
                    reference.RepresentativeFaceId
                })
                .ToDictionary(
                    group => group.Key.PersonId,
                    group => new PersonReferenceSet(
                        group.Key.PersonId,
                        group.Key.DisplayName,
                        group.Key.RepresentativeFaceId,
                        group.OrderByDescending(reference => reference.QualityScore)
                            .ThenByDescending(reference => reference.AssignedAtUtc)
                            .Take(Math.Clamp(_options.ReferenceFacesPerPerson, 1, 50))
                            .Select(reference => reference.Embedding)
                            .ToList()));
            if (references.Count == 0)
            {
                continue;
            }

            var rejectedRows = await _db.FaceReviewDecisions
                .AsNoTracking()
                .Where(decision => faceIds.Contains(decision.MediaFaceId)
                                   && decision.CandidatePersonId.HasValue
                                   && decision.ModelKey == modelGroup.Key.ModelKey
                                   && decision.ModelVersion == modelGroup.Key.ModelVersion
                                   && decision.Decision == FaceReviewDecisionType.Rejected)
                .Select(decision => new
                {
                    decision.MediaFaceId,
                    PersonId = decision.CandidatePersonId!.Value
                })
                .ToListAsync(cancellationToken);
            var rejectedByFace = rejectedRows
                .GroupBy(row => row.MediaFaceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.PersonId).ToHashSet());

            // A confirmed person may appear only once in one photograph. Excluding people
            // already assigned elsewhere in the same asset prevents duplicate suggestions.
            var assignedRows = await (
                    from assignment in _db.PersonFaces.AsNoTracking()
                    join face in _db.Faces.AsNoTracking()
                        on assignment.MediaFaceId equals face.Id
                    where assignment.RemovedAtUtc == null
                          && assetIds.Contains(face.MediaAssetId)
                    select new
                    {
                        face.MediaAssetId,
                        assignment.MediaPersonId
                    })
                .Distinct()
                .ToListAsync(cancellationToken);
            var assignedByAsset = assignedRows
                .GroupBy(row => row.MediaAssetId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.MediaPersonId).ToHashSet());

            foreach (var input in groupInputs)
            {
                var rejected = rejectedByFace.GetValueOrDefault(input.FaceId)
                               ?? new HashSet<Guid>();
                var alreadyPresent = assignedByAsset.GetValueOrDefault(input.AssetId)
                                     ?? new HashSet<Guid>();

                var candidates = references.Values
                    .Where(reference => !rejected.Contains(reference.PersonId)
                                        && !alreadyPresent.Contains(reference.PersonId))
                    .Select(reference =>
                    {
                        var score = FaceSimilarityScoring.ScoreReferences(
                            input.Embedding,
                            reference.Embeddings.Select(vector => (IReadOnlyList<float>)vector),
                            reference.Embeddings.Count);
                        return new FaceCandidate(
                            reference.PersonId,
                            reference.DisplayName,
                            reference.RepresentativeFaceId,
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

                results[input.FaceId] = candidates;
            }
        }

        return results;
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
                  && (excludedFaceId == Guid.Empty || assignment.MediaFaceId != excludedFaceId)
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

    private sealed record PersonReferenceSet(
        Guid PersonId,
        string DisplayName,
        Guid? RepresentativeFaceId,
        IReadOnlyList<float[]> Embeddings);
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
