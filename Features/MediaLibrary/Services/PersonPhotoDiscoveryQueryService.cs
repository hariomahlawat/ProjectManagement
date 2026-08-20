using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Projects all meaningful existing person-specific evidence into a person-first review
/// workspace. Direct known-person candidates and current identity-group candidates are
/// combined, de-duplicated and ranked, but never auto-confirmed.
/// </summary>
public sealed class PersonPhotoDiscoveryQueryService : IPersonPhotoDiscoveryQueryService
{
    private const int MaximumCandidateRowsRead = 1000;

    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IFaceIdentityGroupingRuntimeState _groupingState;
    private readonly MediaPeopleOptions _options;

    public PersonPhotoDiscoveryQueryService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IFaceIdentityGroupingRuntimeState groupingState,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _groupingState = groupingState ?? throw new ArgumentNullException(nameof(groupingState));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PersonPhotoDiscoverySummary?> GetSummaryAsync(
        Guid personId,
        bool includeDiscoveryState,
        CancellationToken cancellationToken)
    {
        if (personId == Guid.Empty) return null;

        var person = await _db.Persons.AsNoTracking()
            .Where(item => item.Id == personId
                           && item.Status == MediaPersonStatus.Confirmed
                           && !item.IsHidden)
            .Select(item => new { item.Id, item.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);
        if (person is null) return null;

        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var confirmedRows =
            from assignment in _db.PersonFaces.AsNoTracking()
            join face in _db.Faces.AsNoTracking() on assignment.MediaFaceId equals face.Id
            join asset in _db.Assets.AsNoTracking() on face.MediaAssetId equals asset.Id
            where assignment.MediaPersonId == personId
                  && assignment.RemovedAtUtc == null
                  && !face.IsSuppressed
                  && asset.Kind == MediaAssetKind.Photo
                  && visibleAssetIds.Contains(asset.Id)
            select new { asset.Id, asset.MediaDateUtc };

        var confirmedPhotoCount = await confirmedRows.Select(row => row.Id).Distinct().CountAsync(cancellationToken);
        var latestMediaDateUtc = confirmedPhotoCount == 0
            ? (DateTimeOffset?)null
            : await confirmedRows.MaxAsync(row => (DateTimeOffset?)row.MediaDateUtc, cancellationToken);

        if (!includeDiscoveryState)
        {
            return new PersonPhotoDiscoverySummary(
                person.Id, person.DisplayName, confirmedPhotoCount, latestMediaDateUtc,
                0, 0, 0, 0);
        }

        var trustedReferenceCount = await BuildValidTrustedReferenceFacesQuery(
                _db,
                personId,
                _options.Embedder.Key,
                _options.Embedder.Version,
                _options.Embedder.EmbeddingDimension,
                visibleAssetIds,
                _options.CandidateMinimumTrustedReferenceQuality)
            .CountAsync(cancellationToken);

        var directAssetIds = trustedReferenceCount == 0
            ? Array.Empty<long>()
            : await BuildCandidateRowsQuery(
                    _db,
                    personId,
                    _options.Embedder.Key,
                    _options.Embedder.Version,
                    visibleAssetIds)
                .Select(row => row.AssetId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

        var groupEvidence = trustedReferenceCount == 0
            ? GroupEvidence.Empty
            : await GetGroupEvidenceAsync(personId, visibleAssetIds, cancellationToken);

        var directAssets = directAssetIds.ToHashSet();
        var groupOnlyCount = groupEvidence.AssetIds.Count(assetId => !directAssets.Contains(assetId));
        var possibleMatchCount = directAssets.Count + groupOnlyCount;

        var reviewableFaces = MediaPeopleQueryService.BuildReviewableFacesQuery(_db)
            .Where(face => visibleAssetIds.Contains(face.MediaAssetId));
        var backgroundMatchingCount = await reviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Pending
            || face.CandidateSearchStatus == FaceCandidateSearchStatus.Processing,
            cancellationToken);
        var matchingFailureCount = await reviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Failed,
            cancellationToken);

        return new PersonPhotoDiscoverySummary(
            person.Id,
            person.DisplayName,
            confirmedPhotoCount,
            latestMediaDateUtc,
            trustedReferenceCount,
            possibleMatchCount,
            backgroundMatchingCount,
            matchingFailureCount,
            directAssets.Count,
            groupOnlyCount,
            groupEvidence.Groups.Count);
    }

    public async Task<PersonPhotoDiscoveryResult?> GetCandidatesAsync(
        Guid personId,
        int limit,
        CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(personId, includeDiscoveryState: true, cancellationToken);
        if (summary is null) return null;
        if (!summary.HasTrustedReference)
        {
            return EmptyResult(summary);
        }

        var take = Math.Clamp(limit, 1, 120);
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var directRows = await BuildCandidateRowsQuery(
                _db,
                personId,
                _options.Embedder.Key,
                _options.Embedder.Version,
                visibleAssetIds)
            .OrderByDescending(row => row.Similarity)
            .ThenByDescending(row => row.QualityScore)
            .ThenByDescending(row => row.MediaDateUtc)
            .Take(MaximumCandidateRowsRead)
            .ToListAsync(cancellationToken);

        // Detector overlap can create more than one candidate face for one photograph.
        // Keep the strongest direct appearance from that photograph.
        var direct = directRows
            .GroupBy(row => row.AssetId)
            .Select(group => group
                .OrderByDescending(row => row.Similarity ?? double.NegativeInfinity)
                .ThenByDescending(row => row.QualityScore)
                .ThenBy(row => row.FaceId)
                .First())
            .Select(MapDirectCandidate)
            .OrderBy(candidate => candidate.Band)
            .ThenByDescending(candidate => candidate.Similarity)
            .ThenByDescending(candidate => candidate.MediaDateUtc)
            .ToList();

        var directFaceIds = direct.Select(item => item.FaceId).ToHashSet();
        var directAssetIds = direct.Select(item => item.AssetId).ToHashSet();
        var groupEvidence = await GetGroupEvidenceAsync(personId, visibleAssetIds, cancellationToken);
        var groupCandidates = groupEvidence.Groups
            .Select(group =>
            {
                var remainingCandidates = group.Candidates
                    .Where(candidate => !directFaceIds.Contains(candidate.FaceId)
                                        && !directAssetIds.Contains(candidate.AssetId))
                    .OrderByDescending(candidate => candidate.SimilarityToGroupRepresentative)
                    .ThenByDescending(candidate => candidate.QualityScore)
                    .ToArray();
                return new PersonPhotoIdentityGroupCandidate(
                    group.GroupKey,
                    group.PersonSimilarity,
                    group.CohesionScore,
                    remainingCandidates.Select(candidate => candidate.AssetId).Distinct().Count(),
                    group.FirstSeenUtc,
                    group.LastSeenUtc,
                    remainingCandidates);
            })
            .Where(group => group.Candidates.Count > 0)
            .OrderByDescending(group => group.PersonSimilarity)
            .ThenByDescending(group => group.Candidates.Count)
            .ToArray();

        var strong = direct.Where(item => item.Band == PersonPhotoDiscoveryBand.Strong).Take(take).ToArray();
        var remaining = Math.Max(0, take - strong.Length);
        var moderate = direct.Where(item => item.Band == PersonPhotoDiscoveryBand.Moderate).Take(remaining).ToArray();
        remaining = Math.Max(0, remaining - moderate.Length);
        var other = direct.Where(item => item.Band == PersonPhotoDiscoveryBand.Other).Take(remaining).ToArray();

        var uniqueGroupPhotos = groupCandidates.SelectMany(group => group.Candidates)
            .Select(item => item.AssetId)
            .Distinct()
            .Count();
        var totalDirect = direct.Count;
        var total = totalDirect + uniqueGroupPhotos;
        var updatedSummary = summary with
        {
            PossibleMatchCount = total,
            DirectCandidateCount = totalDirect,
            GroupCandidateAppearanceCount = uniqueGroupPhotos,
            GroupCandidateCount = groupCandidates.Length
        };

        return new PersonPhotoDiscoveryResult(
            updatedSummary,
            strong,
            moderate,
            other,
            groupCandidates,
            total,
            totalDirect,
            uniqueGroupPhotos);
    }

    public async Task<IReadOnlyDictionary<Guid, PersonPhotoCandidate>> GetEligibleCandidatesAsync(
        Guid personId,
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken)
    {
        var selected = (faceIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(Math.Clamp(_options.CandidateBatchConfirmationLimit, 1, 100))
            .ToArray();
        if (personId == Guid.Empty || selected.Length == 0)
        {
            return new Dictionary<Guid, PersonPhotoCandidate>();
        }

        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var hasTrustedReference = await BuildValidTrustedReferenceFacesQuery(
                _db,
                personId,
                _options.Embedder.Key,
                _options.Embedder.Version,
                _options.Embedder.EmbeddingDimension,
                visibleAssetIds,
                _options.CandidateMinimumTrustedReferenceQuality)
            .AnyAsync(cancellationToken);
        if (!hasTrustedReference)
        {
            return new Dictionary<Guid, PersonPhotoCandidate>();
        }

        var directRows = await BuildCandidateRowsQuery(
                _db,
                personId,
                _options.Embedder.Key,
                _options.Embedder.Version,
                visibleAssetIds)
            .Where(row => selected.Contains(row.FaceId))
            .ToListAsync(cancellationToken);

        var result = directRows
            .GroupBy(row => row.FaceId)
            .ToDictionary(
                group => group.Key,
                group => MapDirectCandidate(group
                    .OrderByDescending(row => row.Similarity ?? double.NegativeInfinity)
                    .First()));

        var missing = selected.Where(faceId => !result.ContainsKey(faceId)).ToArray();
        if (missing.Length > 0)
        {
            var groupEvidence = await GetGroupEvidenceAsync(personId, visibleAssetIds, cancellationToken);
            foreach (var candidate in groupEvidence.Groups
                         .SelectMany(group => group.Candidates)
                         .Where(candidate => missing.Contains(candidate.FaceId)))
            {
                result.TryAdd(candidate.FaceId, candidate);
            }
        }

        return result;
    }

    internal static IQueryable<PersonPhotoDiscoveryDatabaseRow> BuildCandidateRowsQuery(
        MediaLibraryDbContext db,
        Guid personId,
        string modelKey,
        string modelVersion,
        IQueryable<long>? visibleAssetIds = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows =
            from decision in db.FaceReviewDecisions.AsNoTracking()
            join face in db.Faces.AsNoTracking() on decision.MediaFaceId equals face.Id
            join asset in db.Assets.AsNoTracking() on face.MediaAssetId equals asset.Id
            join person in db.Persons.AsNoTracking() on decision.CandidatePersonId equals (Guid?)person.Id
            where decision.CandidatePersonId == personId
                  && decision.Decision == FaceReviewDecisionType.Pending
                  && decision.ModelKey == modelKey
                  && decision.ModelVersion == modelVersion
                  && decision.Similarity.HasValue
                  && face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
                  && asset.Kind == MediaAssetKind.Photo
                  && !face.IsSuppressed
                  && face.QualityStatus != FaceQualityStatus.ProcessingFailed
                  && person.Status == MediaPersonStatus.Confirmed
                  && !person.IsHidden
                  && !db.PersonFaces.Any(assignment => assignment.MediaFaceId == face.Id && assignment.RemovedAtUtc == null)
                  && !db.PersonFaces.Any(assignment => assignment.MediaPersonId == personId
                                                      && assignment.RemovedAtUtc == null
                                                      && assignment.MediaFace.MediaAssetId == asset.Id)
                  && !db.FaceReviewDecisions.Any(review => review.MediaFaceId == face.Id
                                                          && !review.CandidatePersonId.HasValue
                                                          && review.Decision == FaceReviewDecisionType.Ignored)
            select new PersonPhotoDiscoveryDatabaseRow
            {
                DecisionId = decision.Id,
                FaceId = face.Id,
                AssetId = asset.Id,
                ContextTitle = asset.ContextTitle,
                ContextSubtitle = asset.ContextSubtitle,
                MediaDateUtc = asset.MediaDateUtc,
                QualityScore = face.QualityScore,
                Similarity = decision.Similarity,
                BestReferenceSimilarity = decision.BestReferenceSimilarity,
                MeanTopSimilarity = decision.MeanTopSimilarity,
                ReferenceCount = decision.ReferenceCount,
                MarginToNext = decision.MarginToNext,
                MarginAvailable = decision.MarginAvailable,
                ConfidenceLevel = decision.ConfidenceLevel,
                DecisionConcurrencyToken = decision.ConcurrencyToken
            };

        if (visibleAssetIds is not null) rows = rows.Where(row => visibleAssetIds.Contains(row.AssetId));
        return rows;
    }

    internal static IQueryable<Guid> BuildValidTrustedReferenceFacesQuery(
        MediaLibraryDbContext db,
        Guid personId,
        string modelKey,
        string modelVersion,
        int dimension,
        IQueryable<long>? visibleAssetIds = null,
        double minimumReferenceQuality = 0d)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query =
            from assignment in db.PersonFaces.AsNoTracking()
            join face in db.Faces.AsNoTracking() on assignment.MediaFaceId equals face.Id
            where assignment.MediaPersonId == personId
                  && assignment.RemovedAtUtc == null
                  && assignment.ReferenceStatus == FaceReferenceStatus.TrustedReference
                  && !face.IsSuppressed
                  && (face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                      || face.QualityStatus == FaceQualityStatus.Detected
                      || face.QualityStatus == FaceQualityStatus.CropIncomplete
                      || face.QualityStatus == FaceQualityStatus.Occluded)
                  && face.QualityScore >= minimumReferenceQuality
                  && face.Embeddings.Any(embedding => embedding.InvalidatedAtUtc == null
                                                      && embedding.ModelKey == modelKey
                                                      && embedding.ModelVersion == modelVersion
                                                      && embedding.Dimension == dimension)
            select new { face.Id, face.MediaAssetId };

        if (visibleAssetIds is not null) query = query.Where(row => visibleAssetIds.Contains(row.MediaAssetId));
        return query.Select(row => row.Id).Distinct();
    }

    private async Task<GroupEvidence> GetGroupEvidenceAsync(
        Guid personId,
        IQueryable<long> visibleAssetIds,
        CancellationToken cancellationToken)
    {
        var snapshot = _groupingState.GetSnapshot();
        var sourceGroups = snapshot.Result?.Groups
            .Select(group => (
                Group: group,
                Candidate: group.Candidates
                    .Where(candidate => candidate.PersonId == personId)
                    .OrderByDescending(candidate => candidate.Similarity)
                    .FirstOrDefault()))
            .Where(item => item.Candidate is not null)
            .OrderByDescending(item => item.Candidate!.Similarity)
            .ToArray()
            ?? Array.Empty<(FaceIdentityGroup Group, FaceCandidate? Candidate)>();

        if (sourceGroups.Length == 0) return GroupEvidence.Empty;

        var allFaceIds = sourceGroups
            .SelectMany(item => item.Group.FaceIds)
            .Distinct()
            .ToArray();

        var rows = await (
                from face in _db.Faces.AsNoTracking()
                join asset in _db.Assets.AsNoTracking() on face.MediaAssetId equals asset.Id
                where allFaceIds.Contains(face.Id)
                      && visibleAssetIds.Contains(asset.Id)
                      && asset.Kind == MediaAssetKind.Photo
                      && !face.IsSuppressed
                      && !_db.PersonFaces.Any(assignment => assignment.MediaFaceId == face.Id && assignment.RemovedAtUtc == null)
                      && !_db.FaceReviewDecisions.Any(review => review.MediaFaceId == face.Id
                                                               && review.CandidatePersonId == personId
                                                               && review.Decision == FaceReviewDecisionType.Rejected
                                                               && review.ModelKey == _options.Embedder.Key
                                                               && review.ModelVersion == _options.Embedder.Version)
                      && !_db.FaceReviewDecisions.Any(review => review.MediaFaceId == face.Id
                                                               && !review.CandidatePersonId.HasValue
                                                               && review.Decision == FaceReviewDecisionType.Ignored)
                select new GroupFaceDatabaseRow
                {
                    FaceId = face.Id,
                    AssetId = asset.Id,
                    ContextTitle = asset.ContextTitle,
                    ContextSubtitle = asset.ContextSubtitle,
                    MediaDateUtc = asset.MediaDateUtc,
                    QualityScore = face.QualityScore
                })
            .ToListAsync(cancellationToken);
        var rowsByFace = rows.ToDictionary(row => row.FaceId);

        var groups = new List<PersonPhotoIdentityGroupCandidate>();
        foreach (var source in sourceGroups)
        {
            var group = source.Group;
            var personCandidate = source.Candidate!;
            var members = group.Members
                .Where(member => rowsByFace.ContainsKey(member.FaceId))
                .GroupBy(member => member.AssetId)
                .Select(assetGroup => assetGroup
                    .OrderByDescending(member => member.SimilarityToRepresentative)
                    .ThenByDescending(member => member.QualityScore)
                    .First())
                .Select(member =>
                {
                    var row = rowsByFace[member.FaceId];
                    return new PersonPhotoCandidate(
                        0,
                        row.FaceId,
                        row.AssetId,
                        row.ContextTitle,
                        row.ContextSubtitle,
                        row.MediaDateUtc,
                        row.QualityScore,
                        personCandidate.Similarity,
                        personCandidate.BestReferenceSimilarity,
                        personCandidate.MeanTopSimilarity,
                        personCandidate.ReferenceCount,
                        personCandidate.MarginToNext,
                        personCandidate.MarginAvailable,
                        personCandidate.ConfidenceLevel,
                        Guid.Empty,
                        PersonPhotoDiscoveryEvidenceSource.IdentityGroupCandidate,
                        ClassifyBand(personCandidate.Similarity, personCandidate.ConfidenceLevel),
                        group.GroupKey,
                        personCandidate.Similarity,
                        member.SimilarityToRepresentative);
                })
                .OrderByDescending(candidate => candidate.SimilarityToGroupRepresentative)
                .ToArray();
            if (members.Length == 0) continue;
            groups.Add(new PersonPhotoIdentityGroupCandidate(
                group.GroupKey,
                personCandidate.Similarity,
                group.CohesionScore,
                members.Select(item => item.AssetId).Distinct().Count(),
                group.FirstSeenUtc,
                group.LastSeenUtc,
                members));
        }

        // A person can occur at most once in one photograph. If more than one identity
        // group points to the same target person in the same photograph, keep the member
        // from the strongest group-level person evidence so Find More Photos never shows
        // duplicate photograph suggestions.
        var seenAssets = new HashSet<long>();
        var normalizedGroups = groups
            .OrderByDescending(group => group.PersonSimilarity)
            .Select(group => group with
            {
                Candidates = group.Candidates
                    .Where(candidate => seenAssets.Add(candidate.AssetId))
                    .ToArray()
            })
            .Where(group => group.Candidates.Count > 0)
            .ToArray();
        var faceIds = normalizedGroups.SelectMany(group => group.Candidates).Select(item => item.FaceId).Distinct().ToHashSet();
        var assetIds = normalizedGroups.SelectMany(group => group.Candidates).Select(item => item.AssetId).Distinct().ToHashSet();
        return new GroupEvidence(normalizedGroups, faceIds, assetIds);
    }

    private IQueryable<long> BuildVisibleAssetIdsQuery()
        => _visibility.Apply(_db.Assets.AsNoTracking()).Select(asset => asset.Id);

    private PersonPhotoCandidate MapDirectCandidate(PersonPhotoDiscoveryDatabaseRow row)
    {
        var similarity = row.Similarity ?? 0d;
        return new PersonPhotoCandidate(
            row.DecisionId,
            row.FaceId,
            row.AssetId,
            row.ContextTitle,
            row.ContextSubtitle,
            row.MediaDateUtc,
            row.QualityScore,
            similarity,
            row.BestReferenceSimilarity ?? similarity,
            row.MeanTopSimilarity ?? similarity,
            row.ReferenceCount,
            row.MarginToNext,
            row.MarginAvailable,
            row.ConfidenceLevel,
            row.DecisionConcurrencyToken,
            PersonPhotoDiscoveryEvidenceSource.DirectPersonCandidate,
            ClassifyBand(similarity, row.ConfidenceLevel));
    }

    private PersonPhotoDiscoveryBand ClassifyBand(double similarity, FaceCandidateConfidenceLevel confidence)
        => ClassifyBandStatic(similarity, confidence, _options);

    private static PersonPhotoDiscoveryBand ClassifyBandStatic(
        double similarity,
        FaceCandidateConfidenceLevel confidence,
        MediaPeopleOptions? options)
    {
        var strong = options?.CandidateStrongSimilarityThreshold ?? 0.72d;
        var moderate = options?.CandidateSimilarityThreshold ?? 0.58d;
        if (confidence == FaceCandidateConfidenceLevel.Strong || similarity >= strong) return PersonPhotoDiscoveryBand.Strong;
        if (similarity >= moderate) return PersonPhotoDiscoveryBand.Moderate;
        return PersonPhotoDiscoveryBand.Other;
    }

    private static PersonPhotoDiscoveryResult EmptyResult(PersonPhotoDiscoverySummary summary)
        => new(
            summary,
            Array.Empty<PersonPhotoCandidate>(),
            Array.Empty<PersonPhotoCandidate>(),
            Array.Empty<PersonPhotoCandidate>(),
            Array.Empty<PersonPhotoIdentityGroupCandidate>(),
            0, 0, 0);

    private sealed record GroupEvidence(
        IReadOnlyList<PersonPhotoIdentityGroupCandidate> Groups,
        IReadOnlySet<Guid> FaceIds,
        IReadOnlySet<long> AssetIds)
    {
        public static readonly GroupEvidence Empty = new(
            Array.Empty<PersonPhotoIdentityGroupCandidate>(),
            new HashSet<Guid>(),
            new HashSet<long>());
    }

    private sealed class GroupFaceDatabaseRow
    {
        public Guid FaceId { get; init; }
        public long AssetId { get; init; }
        public string ContextTitle { get; init; } = string.Empty;
        public string ContextSubtitle { get; init; } = string.Empty;
        public DateTimeOffset MediaDateUtc { get; init; }
        public double QualityScore { get; init; }
    }
}
