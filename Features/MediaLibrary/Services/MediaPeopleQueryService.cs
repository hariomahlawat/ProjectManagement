using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaPeopleQueryService : IMediaPeopleQueryService
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaPeopleOptions _options;

    public MediaPeopleQueryService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<MediaPeopleIndexResult> GetIndexAsync(
        MediaPeopleIndexQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageSize = Math.Clamp(request.PageSize, 12, 120);
        var peopleQuery = _db.Persons
            .AsNoTracking()
            .Where(person => person.Status == MediaPersonStatus.Confirmed
                             || person.Status == MediaPersonStatus.Hidden);
        if (!request.IncludeHidden)
        {
            peopleQuery = peopleQuery.Where(person => !person.IsHidden);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim();
            peopleQuery = peopleQuery.Where(person =>
                EF.Functions.ILike(person.DisplayName, $"%{EscapeLikePattern(term)}%", "\\"));
        }

        var projected = peopleQuery.Select(person => new MediaPersonCard(
            person.Id,
            person.DisplayName,
            person.RepresentativeFaceId,
            person.FaceAssignments.Count(assignment => assignment.RemovedAtUtc == null),
            person.FaceAssignments
                .Where(assignment => assignment.RemovedAtUtc == null
                                     && assignment.MediaFace.MediaAsset.IsAvailable
                                     && !assignment.MediaFace.MediaAsset.IsDeleted
                                     && !assignment.MediaFace.MediaAsset.IsArchived)
                .Select(assignment => assignment.MediaFace.MediaAssetId)
                .Distinct()
                .Count(),
            person.FaceAssignments
                .Where(assignment => assignment.RemovedAtUtc == null
                                     && assignment.MediaFace.MediaAsset.IsAvailable
                                     && !assignment.MediaFace.MediaAsset.IsDeleted
                                     && !assignment.MediaFace.MediaAsset.IsArchived)
                .Select(assignment => (DateTimeOffset?)assignment.MediaFace.MediaAsset.MediaDateUtc)
                .Max(),
            person.IsHidden,
            person.IsMinor,
            person.ConcurrencyToken));

        projected = request.Sort switch
        {
            "photos" => projected.OrderByDescending(person => person.PhotoCount)
                .ThenBy(person => person.DisplayName),
            "recent" => projected.OrderByDescending(person => person.LatestMediaDateUtc)
                .ThenBy(person => person.DisplayName),
            _ => projected.OrderBy(person => person.DisplayName)
        };

        var total = await projected.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var pageNumber = Math.Clamp(request.PageNumber, 1, pageCount);
        var people = await projected
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var reviewableFaces = BuildReviewableFacesQuery();
        var pendingReviewCount = await reviewableFaces.CountAsync(cancellationToken);
        var unidentifiedFaceCount = await _db.Faces
            .AsNoTracking()
            .CountAsync(face => !face.IsSuppressed
                                && face.MediaAsset.IsAvailable
                                && !face.MediaAsset.IsDeleted
                                && !face.MediaAsset.IsArchived
                                && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)
                                && face.QualityStatus != FaceQualityStatus.ProcessingFailed,
                cancellationToken);

        return new MediaPeopleIndexResult(
            people,
            total,
            pendingReviewCount,
            unidentifiedFaceCount,
            pageNumber,
            pageSize,
            pageNumber > 1,
            pageNumber < pageCount);
    }

    public async Task<MediaPersonDetailsResult?> GetPersonAsync(
        Guid personId,
        CancellationToken cancellationToken)
    {
        var person = await _db.Persons
            .AsNoTracking()
            .Where(item => item.Id == personId
                           && (item.Status == MediaPersonStatus.Confirmed
                               || item.Status == MediaPersonStatus.Hidden))
            .Select(item => new
            {
                item.Id,
                item.DisplayName,
                item.RepresentativeFaceId,
                item.IsHidden,
                item.IsMinor,
                item.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (person is null)
        {
            return null;
        }

        var assignments = await _db.PersonFaces
            .AsNoTracking()
            .Where(assignment => assignment.MediaPersonId == personId
                                 && assignment.RemovedAtUtc == null
                                 && !assignment.MediaFace.IsSuppressed
                                 && assignment.MediaFace.MediaAsset.IsAvailable
                                 && !assignment.MediaFace.MediaAsset.IsDeleted
                                 && !assignment.MediaFace.MediaAsset.IsArchived)
            .OrderByDescending(assignment => assignment.MediaFace.MediaAsset.MediaDateUtc)
            .ThenBy(assignment => assignment.MediaFace.MediaAssetId)
            .Select(assignment => new MediaPersonPhotoItem(
                assignment.MediaFace.MediaAssetId,
                assignment.MediaFaceId,
                assignment.MediaFace.MediaAsset.ContextTitle,
                assignment.MediaFace.MediaAsset.ContextSubtitle,
                assignment.MediaFace.MediaAsset.SourceLabel,
                assignment.MediaFace.MediaAsset.MediaDateUtc,
                assignment.MediaFace.MediaAsset.Width,
                assignment.MediaFace.MediaAsset.Height,
                assignment.MediaFace.QualityScore,
                person.RepresentativeFaceId == assignment.MediaFaceId))
            .ToListAsync(cancellationToken);
        var mergeTargets = await GetPersonOptionsAsync(cancellationToken);
        mergeTargets = mergeTargets.Where(item => item.Id != personId).ToList();

        return new MediaPersonDetailsResult(
            person.Id,
            person.DisplayName,
            person.RepresentativeFaceId,
            person.IsHidden,
            person.IsMinor,
            person.ConcurrencyToken,
            assignments.Count,
            assignments.Select(item => item.AssetId).Distinct().Count(),
            assignments.Count == 0 ? null : assignments.Min(item => item.MediaDateUtc),
            assignments.Count == 0 ? null : assignments.Max(item => item.MediaDateUtc),
            assignments,
            mergeTargets);
    }

    public async Task<FaceReviewQueueResult> GetReviewQueueAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 12, 100);
        var reviewableFaces = BuildReviewableFacesQuery();

        var totalFaces = await reviewableFaces.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalFaces / (double)pageSize));
        pageNumber = Math.Clamp(pageNumber, 1, pageCount);
        var faces = await reviewableFaces
            .OrderByDescending(face => _db.FaceReviewDecisions.Any(decision =>
                decision.MediaFaceId == face.Id
                && decision.Decision == FaceReviewDecisionType.Pending
                && decision.CandidatePersonId.HasValue))
            .ThenByDescending(face => face.QualityScore)
            .ThenByDescending(face => face.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(face => new ReviewFaceRow(
                face.Id,
                face.MediaAssetId,
                face.MediaAsset.ContextTitle,
                face.MediaAsset.ContextSubtitle,
                face.MediaAsset.MediaDateUtc,
                face.QualityScore))
            .ToListAsync(cancellationToken);

        var faceIds = faces.Select(face => face.FaceId).ToArray();
        var candidateRows = faceIds.Length == 0
            ? new List<ReviewCandidateRow>()
            : await _db.FaceReviewDecisions
                .AsNoTracking()
                .Where(decision => faceIds.Contains(decision.MediaFaceId)
                                   && decision.Decision == FaceReviewDecisionType.Pending
                                   && decision.CandidatePersonId.HasValue
                                   && decision.CandidatePerson != null
                                   && !decision.CandidatePerson.IsHidden
                                   && decision.CandidatePerson.Status == MediaPersonStatus.Confirmed)
                .OrderBy(decision => decision.MediaFaceId)
                .ThenByDescending(decision => decision.Similarity)
                .Select(decision => new ReviewCandidateRow(
                    decision.MediaFaceId,
                    decision.Id,
                    decision.CandidatePersonId!.Value,
                    decision.CandidatePerson!.DisplayName,
                    decision.Similarity,
                    decision.ConcurrencyToken))
                .ToListAsync(cancellationToken);
        var candidatesByFace = candidateRows
            .GroupBy(candidate => candidate.FaceId)
            .ToDictionary(
                group => group.Key,
                group => BuildCandidateItems(group.ToList()));
        var items = faces
            .Select(face => new FaceReviewQueueItem(
                face.FaceId,
                face.AssetId,
                face.ContextTitle,
                face.ContextSubtitle,
                face.MediaDateUtc,
                face.QualityScore,
                candidatesByFace.GetValueOrDefault(face.FaceId)
                    ?? Array.Empty<FaceReviewCandidateItem>()))
            .ToList();

        var availablePeople = await GetPersonOptionsAsync(cancellationToken);
        return new FaceReviewQueueResult(
            items,
            availablePeople,
            totalFaces,
            pageNumber,
            pageSize,
            pageNumber > 1,
            pageNumber < pageCount);
    }

    public async Task<IReadOnlyList<MediaPersonOption>> GetPersonOptionsAsync(
        CancellationToken cancellationToken)
        => await _db.Persons
            .AsNoTracking()
            .Where(person => !person.IsHidden && person.Status == MediaPersonStatus.Confirmed)
            .OrderBy(person => person.DisplayName)
            .Select(person => new MediaPersonOption(person.Id, person.DisplayName))
            .ToListAsync(cancellationToken);

    private IQueryable<MediaFace> BuildReviewableFacesQuery()
        => _db.Faces
            .AsNoTracking()
            .Where(face => !face.IsSuppressed
                           && face.QualityStatus != FaceQualityStatus.ProcessingFailed
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)
                           && !_db.FaceReviewDecisions.Any(decision =>
                               decision.MediaFaceId == face.Id
                               && !decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Ignored));

    private IReadOnlyList<FaceReviewCandidateItem> BuildCandidateItems(
        IReadOnlyList<ReviewCandidateRow> candidates)
    {
        var ordered = candidates
            .OrderByDescending(candidate => candidate.Similarity ?? double.NegativeInfinity)
            .ThenBy(candidate => candidate.DisplayName)
            .ToList();
        var results = new List<FaceReviewCandidateItem>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var candidate = ordered[index];
            var nextSimilarity = index + 1 < ordered.Count ? ordered[index + 1].Similarity : null;
            var margin = candidate.Similarity.HasValue && nextSimilarity.HasValue
                ? candidate.Similarity.Value - nextSimilarity.Value
                : candidate.Similarity;
            var isStrong = candidate.Similarity >= _options.CandidateStrongSimilarityThreshold
                           && (!margin.HasValue || margin.Value >= _options.CandidateMinimumMargin);
            var isAmbiguous = index == 0
                              && candidate.Similarity >= _options.CandidateSimilarityThreshold
                              && !isStrong;
            results.Add(new FaceReviewCandidateItem(
                candidate.DecisionId,
                candidate.PersonId,
                candidate.DisplayName,
                candidate.Similarity,
                candidate.ConcurrencyToken,
                index + 1,
                margin,
                isStrong,
                isAmbiguous));
        }

        return results;
    }

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record ReviewFaceRow(
        Guid FaceId,
        long AssetId,
        string ContextTitle,
        string ContextSubtitle,
        DateTimeOffset MediaDateUtc,
        double QualityScore);

    private sealed record ReviewCandidateRow(
        Guid FaceId,
        long DecisionId,
        Guid PersonId,
        string DisplayName,
        double? Similarity,
        Guid ConcurrencyToken);
}
