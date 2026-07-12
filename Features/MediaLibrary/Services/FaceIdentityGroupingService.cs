using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Produces strict, review-only groups of unassigned face embeddings. Groups are derived
/// at query time and are intentionally not identities: only a human batch action creates
/// or assigns a person. Faces from the same photograph are never placed in one group.
/// </summary>
public sealed class FaceIdentityGroupingService : IFaceIdentityGroupingService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IFaceCandidateSearchService _candidateSearch;
    private readonly MediaPeopleOptions _options;

    public FaceIdentityGroupingService(
        MediaLibraryDbContext db,
        IFaceCandidateSearchService candidateSearch,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _candidateSearch = candidateSearch ?? throw new ArgumentNullException(nameof(candidateSearch));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<FaceIdentityGroupingResult> GetGroupsAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.GroupingEnabled)
        {
            return EmptyResult();
        }

        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var dimension = _options.Embedder.EmbeddingDimension;
        var maximumFaces = Math.Clamp(_options.GroupingMaximumFaces, 10, 25_000);

        // Keep the entire relational operation provider-translatable. The earlier navigation
        // SelectMany/custom-record projection caused Npgsql to reject the expression tree at
        // runtime. Ordering and limiting now happen over scalar database columns; construction
        // of the in-memory grouping row happens only after materialisation.
        var databaseRows = await BuildGroupingRowsQuery(
                _db,
                modelKey,
                modelVersion,
                dimension,
                _options.MinimumQualityScore,
                maximumFaces)
            .ToListAsync(cancellationToken);

        var validRows = databaseRows
            .Where(row => row.Embedding is { Length: > 0 }
                          && row.Embedding.Length == dimension)
            .GroupBy(row => row.FaceId)
            .Select(group => group
                .OrderByDescending(row => row.EmbeddingQualityScore)
                .ThenByDescending(row => row.QualityScore)
                .First())
            .Select(row => new GroupingFaceRow(
                row.FaceId,
                row.AssetId,
                row.ContextTitle,
                row.ContextSubtitle,
                row.MediaDateUtc,
                row.QualityScore,
                row.Embedding,
                row.ModelKey,
                row.ModelVersion,
                row.Dimension))
            .ToList();

        if (validRows.Count == 0)
        {
            return EmptyResult();
        }

        var mutableGroups = BuildStrictGroups(validRows, _options);
        var minimumFaces = Math.Clamp(_options.GroupingMinimumFaces, 2, 20);
        var acceptedGroups = mutableGroups
            .Where(group => group.Members.Count >= minimumFaces)
            .OrderByDescending(group => group.Members.Count)
            .ThenByDescending(group => group.Members.Max(member => member.MediaDateUtc))
            .ToList();

        var result = new List<FaceIdentityGroup>(acceptedGroups.Count);
        foreach (var group in acceptedGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            group.RecalculateRepresentative();
            var representative = group.Representative;
            var searchedCandidates = await _candidateSearch.SearchAsync(
                representative.FaceId,
                group.Centroid,
                representative.ModelKey,
                representative.ModelVersion,
                representative.Dimension,
                cancellationToken);

            var memberFaceIds = group.Members.Select(member => member.FaceId).ToArray();
            var rejectedPersonIds = await _db.FaceReviewDecisions
                .AsNoTracking()
                .Where(decision => memberFaceIds.Contains(decision.MediaFaceId)
                                   && decision.CandidatePersonId.HasValue
                                   && decision.Decision == FaceReviewDecisionType.Rejected
                                   && decision.ModelKey == representative.ModelKey
                                   && decision.ModelVersion == representative.ModelVersion)
                .Select(decision => decision.CandidatePersonId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            var rejected = rejectedPersonIds.ToHashSet();
            var candidates = searchedCandidates
                .Where(candidate => !rejected.Contains(candidate.PersonId))
                .ToList();
            var members = group.Members
                .OrderByDescending(member => member.QualityScore)
                .ThenByDescending(member => member.MediaDateUtc)
                .Select(member => new FaceIdentityGroupMember(
                    member.FaceId,
                    member.AssetId,
                    member.ContextTitle,
                    member.ContextSubtitle,
                    member.MediaDateUtc,
                    member.QualityScore,
                    FaceSimilarityScoring.CosineSimilarity(member.Embedding, representative.Embedding)))
                .ToList();
            var cohesion = members.Count == 0
                ? 0d
                : members.Average(member => member.SimilarityToRepresentative);

            result.Add(new FaceIdentityGroup(
                CreateGroupKey(members.Select(member => member.FaceId)),
                representative.FaceId,
                members.Select(member => member.FaceId).ToArray(),
                members,
                candidates,
                cohesion,
                members.Select(member => member.AssetId).Distinct().Count(),
                members.Min(member => member.MediaDateUtc),
                members.Max(member => member.MediaDateUtc)));
        }

        var groupedFaceCount = result.SelectMany(group => group.FaceIds).Distinct().Count();
        return new FaceIdentityGroupingResult(
            result,
            result.Count,
            groupedFaceCount,
            Math.Max(0, validRows.Count - groupedFaceCount));
    }

    /// <summary>
    /// Builds the PostgreSQL-translatable relational query used by identity grouping.
    /// Kept internal so provider translation can be regression-tested with ToQueryString().
    /// </summary>
    internal static IQueryable<GroupingFaceDatabaseRow> BuildGroupingRowsQuery(
        MediaLibraryDbContext db,
        string modelKey,
        string modelVersion,
        int dimension,
        double minimumQualityScore,
        int maximumFaces)
    {
        ArgumentNullException.ThrowIfNull(db);

        var ordered =
            from embedding in db.FaceEmbeddings.AsNoTracking()
            join face in db.Faces.AsNoTracking()
                on embedding.MediaFaceId equals face.Id
            join asset in db.Assets.AsNoTracking()
                on face.MediaAssetId equals asset.Id
            where !face.IsSuppressed
                  && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                  && face.QualityScore >= minimumQualityScore
                  && asset.IsAvailable
                  && !asset.IsDeleted
                  && !asset.IsArchived
                  && embedding.InvalidatedAtUtc == null
                  && embedding.ModelKey == modelKey
                  && embedding.ModelVersion == modelVersion
                  && embedding.Dimension == dimension
                  && !db.PersonFaces.Any(assignment =>
                      assignment.MediaFaceId == face.Id
                      && assignment.RemovedAtUtc == null)
                  && !db.FaceReviewDecisions.Any(decision =>
                      decision.MediaFaceId == face.Id
                      && !decision.CandidatePersonId.HasValue
                      && decision.Decision == FaceReviewDecisionType.Ignored)
            orderby face.QualityScore descending,
                asset.MediaDateUtc descending,
                embedding.QualityScore descending,
                face.Id
            select new
            {
                FaceId = face.Id,
                AssetId = face.MediaAssetId,
                asset.ContextTitle,
                asset.ContextSubtitle,
                asset.MediaDateUtc,
                QualityScore = face.QualityScore,
                EmbeddingQualityScore = embedding.QualityScore,
                embedding.Embedding,
                embedding.ModelKey,
                embedding.ModelVersion,
                embedding.Dimension
            };

        return ordered
            .Take(Math.Clamp(maximumFaces, 1, 25_000))
            .Select(row => new GroupingFaceDatabaseRow
            {
                FaceId = row.FaceId,
                AssetId = row.AssetId,
                ContextTitle = row.ContextTitle,
                ContextSubtitle = row.ContextSubtitle,
                MediaDateUtc = row.MediaDateUtc,
                QualityScore = row.QualityScore,
                EmbeddingQualityScore = row.EmbeddingQualityScore,
                Embedding = row.Embedding,
                ModelKey = row.ModelKey,
                ModelVersion = row.ModelVersion,
                Dimension = row.Dimension
            });
    }

    private static FaceIdentityGroupingResult EmptyResult()
        => new(Array.Empty<FaceIdentityGroup>(), 0, 0, 0);

    private static List<MutableGroup> BuildStrictGroups(
        IReadOnlyList<GroupingFaceRow> rows,
        MediaPeopleOptions options)
    {
        var groups = new List<MutableGroup>();
        var similarityThreshold = options.GroupingSimilarityThreshold;
        var pairwiseThreshold = options.GroupingMinimumPairwiseSimilarity;
        var maximumGroupSize = Math.Clamp(options.GroupingMaximumGroupSize, 2, 500);

        foreach (var row in rows
                     .OrderByDescending(item => item.QualityScore)
                     .ThenByDescending(item => item.MediaDateUtc))
        {
            MutableGroup? bestGroup = null;
            var bestScore = double.NegativeInfinity;
            foreach (var group in groups)
            {
                if (group.Members.Count >= maximumGroupSize
                    || group.AssetIds.Contains(row.AssetId)
                    || group.Centroid.Length != row.Embedding.Length)
                {
                    continue;
                }

                var centroidSimilarity = FaceSimilarityScoring.CosineSimilarity(row.Embedding, group.Centroid);
                if (centroidSimilarity < similarityThreshold)
                {
                    continue;
                }

                // Complete-link evaluation keeps groups cohesive and prevents chaining where
                // A resembles B and B resembles C but A and C are different people.
                var pairwise = group.Members
                    .OrderByDescending(member => member.QualityScore)
                    .Select(member => FaceSimilarityScoring.CosineSimilarity(row.Embedding, member.Embedding))
                    .ToArray();
                if (pairwise.Length == 0 || pairwise.Min() < pairwiseThreshold)
                {
                    continue;
                }

                var score = centroidSimilarity * 0.7d + pairwise.Average() * 0.3d;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestGroup = group;
                }
            }

            if (bestGroup is null)
            {
                groups.Add(new MutableGroup(row));
            }
            else
            {
                bestGroup.Add(row);
            }
        }

        return groups;
    }

    private static string CreateGroupKey(IEnumerable<Guid> faceIds)
    {
        var payload = string.Join(
            '|',
            faceIds.OrderBy(id => id).Select(id => id.ToString("N")));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private sealed record GroupingFaceRow(
        Guid FaceId,
        long AssetId,
        string ContextTitle,
        string ContextSubtitle,
        DateTimeOffset MediaDateUtc,
        double QualityScore,
        float[] Embedding,
        string ModelKey,
        string ModelVersion,
        int Dimension);

    private sealed class MutableGroup
    {
        public MutableGroup(GroupingFaceRow first)
        {
            Members.Add(first);
            AssetIds.Add(first.AssetId);
            Centroid = first.Embedding.ToArray();
            Representative = first;
        }

        public List<GroupingFaceRow> Members { get; } = new();
        public HashSet<long> AssetIds { get; } = new();
        public float[] Centroid { get; private set; }
        public GroupingFaceRow Representative { get; private set; }

        public void Add(GroupingFaceRow row)
        {
            Members.Add(row);
            AssetIds.Add(row.AssetId);
            Centroid = FaceSimilarityScoring.CreateNormalisedCentroid(
                Members.Select(member => (IReadOnlyList<float>)member.Embedding));
            RecalculateRepresentative();
        }

        public void RecalculateRepresentative()
        {
            if (Centroid.Length == 0)
            {
                Representative = Members
                    .OrderByDescending(member => member.QualityScore)
                    .First();
                Centroid = Representative.Embedding.ToArray();
                return;
            }

            Representative = Members
                .OrderByDescending(member =>
                    FaceSimilarityScoring.CosineSimilarity(member.Embedding, Centroid))
                .ThenByDescending(member => member.QualityScore)
                .ThenByDescending(member => member.MediaDateUtc)
                .First();
        }
    }
}

/// <summary>
/// Flat database projection for grouping. This intentionally contains scalar columns only;
/// clustering and identity construction occur after the query has been materialised.
/// </summary>
internal sealed class GroupingFaceDatabaseRow
{
    public Guid FaceId { get; init; }
    public long AssetId { get; init; }
    public string ContextTitle { get; init; } = string.Empty;
    public string ContextSubtitle { get; init; } = string.Empty;
    public DateTimeOffset MediaDateUtc { get; init; }
    public double QualityScore { get; init; }
    public double EmbeddingQualityScore { get; init; }
    public float[] Embedding { get; init; } = Array.Empty<float>();
    public string ModelKey { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public int Dimension { get; init; }
}
