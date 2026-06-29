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
            return new FaceIdentityGroupingResult(Array.Empty<FaceIdentityGroup>(), 0, 0, 0);
        }

        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var dimension = _options.Embedder.EmbeddingDimension;
        var maximumFaces = Math.Clamp(_options.GroupingMaximumFaces, 10, 25_000);
        var rows = await _db.Faces
            .AsNoTracking()
            .Where(face => !face.IsSuppressed
                           && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                           && face.QualityScore >= _options.MinimumQualityScore
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)
                           && !_db.FaceReviewDecisions.Any(decision =>
                               decision.MediaFaceId == face.Id
                               && !decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Ignored))
            .SelectMany(
                face => face.Embeddings
                    .Where(embedding => embedding.InvalidatedAtUtc == null
                                        && embedding.ModelKey == modelKey
                                        && embedding.ModelVersion == modelVersion
                                        && embedding.Dimension == dimension),
                (face, embedding) => new GroupingFaceRow(
                    face.Id,
                    face.MediaAssetId,
                    face.MediaAsset.ContextTitle,
                    face.MediaAsset.ContextSubtitle,
                    face.MediaAsset.MediaDateUtc,
                    face.QualityScore,
                    embedding.Embedding,
                    embedding.ModelKey,
                    embedding.ModelVersion,
                    embedding.Dimension))
            .OrderByDescending(face => face.QualityScore)
            .ThenByDescending(face => face.MediaDateUtc)
            .Take(maximumFaces)
            .ToListAsync(cancellationToken);

        var validRows = rows
            .Where(row => row.Embedding.Length == dimension)
            .GroupBy(row => row.FaceId)
            .Select(group => group
                .OrderByDescending(row => row.QualityScore)
                .First())
            .ToList();
        if (validRows.Count == 0)
        {
            return new FaceIdentityGroupingResult(Array.Empty<FaceIdentityGroup>(), 0, 0, 0);
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
