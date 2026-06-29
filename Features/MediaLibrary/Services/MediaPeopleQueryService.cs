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
        var normalizedQuery = string.IsNullOrWhiteSpace(request.Query)
            ? null
            : request.Query.Trim();
        var normalizedSort = NormalizeSort(request.Sort);

        var filteredPeople = BuildFilteredPeopleQuery(
            _db,
            normalizedQuery,
            request.IncludeHidden);
        var total = await filteredPeople.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var pageNumber = Math.Clamp(request.PageNumber, 1, pageCount);

        // Aggregation, ordering and pagination all remain on scalar SQL columns. The former
        // query projected a MediaPersonCard containing correlated subqueries and then ordered
        // the CLR record; Npgsql correctly rejected that non-translatable expression tree.
        var databaseRows = await BuildPeopleIndexRowsQuery(
                _db,
                filteredPeople,
                normalizedSort,
                (pageNumber - 1) * pageSize,
                pageSize)
            .ToListAsync(cancellationToken);

        var people = databaseRows
            .Select(row => new MediaPersonCard(
                row.Id,
                row.DisplayName,
                row.RepresentativeFaceId,
                row.ConfirmedFaceCount,
                row.PhotoCount,
                row.LatestMediaDateUtc,
                row.IsHidden,
                row.IsMinor,
                row.ConcurrencyToken))
            .ToList();

        var reviewableFaces = BuildReviewableFacesQuery(_db);
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

        var assignmentRows = await (
                from assignment in _db.PersonFaces.AsNoTracking()
                join face in _db.Faces.AsNoTracking()
                    on assignment.MediaFaceId equals face.Id
                join asset in _db.Assets.AsNoTracking()
                    on face.MediaAssetId equals asset.Id
                where assignment.MediaPersonId == personId
                      && assignment.RemovedAtUtc == null
                      && !face.IsSuppressed
                      && asset.IsAvailable
                      && !asset.IsDeleted
                      && !asset.IsArchived
                orderby asset.MediaDateUtc descending, asset.Id, face.Id
                select new MediaPersonPhotoDatabaseRow
                {
                    AssetId = asset.Id,
                    FaceId = face.Id,
                    ContextTitle = asset.ContextTitle,
                    ContextSubtitle = asset.ContextSubtitle,
                    SourceLabel = asset.SourceLabel,
                    MediaDateUtc = asset.MediaDateUtc,
                    Width = asset.Width,
                    Height = asset.Height,
                    FaceQualityScore = face.QualityScore
                })
            .ToListAsync(cancellationToken);

        var assignments = assignmentRows
            .Select(row => new MediaPersonPhotoItem(
                row.AssetId,
                row.FaceId,
                row.ContextTitle,
                row.ContextSubtitle,
                row.SourceLabel,
                row.MediaDateUtc,
                row.Width,
                row.Height,
                row.FaceQualityScore,
                person.RepresentativeFaceId == row.FaceId))
            .ToList();
        var mergeTargets = await GetPersonOptionsAsync(cancellationToken);
        mergeTargets = mergeTargets.Where(item => item.Id != personId).ToList();

        var historyRows = await _db.IdentityAudits
            .AsNoTracking()
            .Where(audit => audit.PersonId == personId
                            || audit.PreviousPersonId == personId
                            || audit.NewPersonId == personId)
            .OrderByDescending(audit => audit.PerformedAtUtc)
            .ThenByDescending(audit => audit.Id)
            .Take(100)
            .Select(audit => new
            {
                audit.Id,
                audit.Action,
                audit.Notes,
                audit.PerformedByUserId,
                audit.PerformedAtUtc,
                audit.FaceId,
                audit.PreviousPersonId,
                audit.NewPersonId
            })
            .ToListAsync(cancellationToken);
        var history = historyRows
            .Select(audit => new MediaIdentityHistoryItem(
                audit.Id,
                audit.Action,
                IdentityActionLabel(audit.Action),
                audit.Notes,
                audit.PerformedByUserId,
                audit.PerformedAtUtc,
                audit.FaceId,
                audit.PreviousPersonId,
                audit.NewPersonId))
            .ToList();

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
            mergeTargets,
            history);
    }

    public async Task<FaceReviewQueueResult> GetReviewQueueAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 12, 100);
        var reviewableFaces = BuildReviewableFacesQuery(_db);

        var totalFaces = await reviewableFaces.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalFaces / (double)pageSize));
        pageNumber = Math.Clamp(pageNumber, 1, pageCount);
        var faceRows = await reviewableFaces
            .OrderByDescending(face => _db.FaceReviewDecisions.Any(decision =>
                decision.MediaFaceId == face.Id
                && decision.Decision == FaceReviewDecisionType.Pending
                && decision.CandidatePersonId.HasValue))
            .ThenByDescending(face => face.QualityScore)
            .ThenByDescending(face => face.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(face => new ReviewFaceDatabaseRow
            {
                FaceId = face.Id,
                AssetId = face.MediaAssetId,
                ContextTitle = face.MediaAsset.ContextTitle,
                ContextSubtitle = face.MediaAsset.ContextSubtitle,
                MediaDateUtc = face.MediaAsset.MediaDateUtc,
                QualityScore = face.QualityScore
            })
            .ToListAsync(cancellationToken);

        var faceIds = faceRows.Select(face => face.FaceId).ToArray();
        var candidateDatabaseRows = faceIds.Length == 0
            ? new List<ReviewCandidateDatabaseRow>()
            : await (
                    from decision in _db.FaceReviewDecisions.AsNoTracking()
                    join person in _db.Persons.AsNoTracking()
                        on decision.CandidatePersonId equals (Guid?)person.Id
                    where faceIds.Contains(decision.MediaFaceId)
                          && decision.Decision == FaceReviewDecisionType.Pending
                          && decision.CandidatePersonId.HasValue
                          && !person.IsHidden
                          && person.Status == MediaPersonStatus.Confirmed
                    orderby decision.MediaFaceId, decision.Similarity descending, person.DisplayName
                    select new ReviewCandidateDatabaseRow
                    {
                        FaceId = decision.MediaFaceId,
                        DecisionId = decision.Id,
                        PersonId = person.Id,
                        DisplayName = person.DisplayName,
                        RepresentativeFaceId = person.RepresentativeFaceId,
                        Similarity = decision.Similarity,
                        ConcurrencyToken = decision.ConcurrencyToken
                    })
                .ToListAsync(cancellationToken);

        var candidatesByFace = candidateDatabaseRows
            .GroupBy(candidate => candidate.FaceId)
            .ToDictionary(
                group => group.Key,
                group => BuildCandidateItems(group
                    .Select(candidate => new ReviewCandidateRow(
                        candidate.FaceId,
                        candidate.DecisionId,
                        candidate.PersonId,
                        candidate.DisplayName,
                        candidate.RepresentativeFaceId,
                        candidate.Similarity,
                        candidate.ConcurrencyToken))
                    .ToList()));
        var items = faceRows
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
    {
        var people = await _db.Persons
            .AsNoTracking()
            .Where(person => !person.IsHidden && person.Status == MediaPersonStatus.Confirmed)
            .OrderBy(person => person.DisplayName)
            .Select(person => new { person.Id, person.DisplayName })
            .ToListAsync(cancellationToken);

        return people
            .Select(person => new MediaPersonOption(person.Id, person.DisplayName))
            .ToList();
    }

    internal static IQueryable<MediaPerson> BuildFilteredPeopleQuery(
        MediaLibraryDbContext db,
        string? query,
        bool includeHidden)
    {
        ArgumentNullException.ThrowIfNull(db);

        var peopleQuery = db.Persons
            .AsNoTracking()
            .Where(person => person.Status == MediaPersonStatus.Confirmed
                             || person.Status == MediaPersonStatus.Hidden);
        if (!includeHidden)
        {
            peopleQuery = peopleQuery.Where(person => !person.IsHidden);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var escapedTerm = EscapeLikePattern(query.Trim());
            peopleQuery = peopleQuery.Where(person =>
                EF.Functions.ILike(person.DisplayName, $"%{escapedTerm}%", "\\"));
        }

        return peopleQuery;
    }

    /// <summary>
    /// Builds the provider-translatable people-directory query. Aggregate subqueries are
    /// joined to people before sorting; MediaPersonCard is constructed only in memory.
    /// </summary>
    internal static IQueryable<MediaPersonIndexDatabaseRow> BuildPeopleIndexRowsQuery(
        MediaLibraryDbContext db,
        IQueryable<MediaPerson> filteredPeople,
        string sort,
        int skip,
        int take)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filteredPeople);

        var activeAssignmentStats =
            from assignment in db.PersonFaces.AsNoTracking()
            where assignment.RemovedAtUtc == null
            group assignment by assignment.MediaPersonId
            into assignmentGroup
            select new
            {
                PersonId = assignmentGroup.Key,
                ConfirmedFaceCount = assignmentGroup.Count()
            };

        var availableAssignmentRows =
            from assignment in db.PersonFaces.AsNoTracking()
            join face in db.Faces.AsNoTracking()
                on assignment.MediaFaceId equals face.Id
            join asset in db.Assets.AsNoTracking()
                on face.MediaAssetId equals asset.Id
            where assignment.RemovedAtUtc == null
                  && !face.IsSuppressed
                  && asset.IsAvailable
                  && !asset.IsDeleted
                  && !asset.IsArchived
            select new
            {
                assignment.MediaPersonId,
                AssetId = asset.Id,
                asset.MediaDateUtc
            };

        var availablePhotoStats =
            from row in availableAssignmentRows
            group row by row.MediaPersonId
            into photoGroup
            select new
            {
                PersonId = photoGroup.Key,
                PhotoCount = photoGroup.Select(row => row.AssetId).Distinct().Count(),
                LatestMediaDateUtc = (DateTimeOffset?)photoGroup.Max(row => row.MediaDateUtc)
            };

        var rows =
            from person in filteredPeople
            join assignmentStats in activeAssignmentStats
                on person.Id equals assignmentStats.PersonId into assignmentStatsGroup
            from assignmentStats in assignmentStatsGroup.DefaultIfEmpty()
            join photoStats in availablePhotoStats
                on person.Id equals photoStats.PersonId into photoStatsGroup
            from photoStats in photoStatsGroup.DefaultIfEmpty()
            select new
            {
                person.Id,
                person.DisplayName,
                person.RepresentativeFaceId,
                ConfirmedFaceCount = (int?)assignmentStats.ConfirmedFaceCount ?? 0,
                PhotoCount = (int?)photoStats.PhotoCount ?? 0,
                LatestMediaDateUtc = photoStats.LatestMediaDateUtc,
                person.IsHidden,
                person.IsMinor,
                person.ConcurrencyToken
            };

        var ordered = NormalizeSort(sort) switch
        {
            "photos" => rows.OrderByDescending(person => person.PhotoCount)
                .ThenBy(person => person.DisplayName)
                .ThenBy(person => person.Id),
            "recent" => rows.OrderByDescending(person => person.LatestMediaDateUtc)
                .ThenBy(person => person.DisplayName)
                .ThenBy(person => person.Id),
            _ => rows.OrderBy(person => person.DisplayName)
                .ThenBy(person => person.Id)
        };

        return ordered
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 120))
            .Select(person => new MediaPersonIndexDatabaseRow
            {
                Id = person.Id,
                DisplayName = person.DisplayName,
                RepresentativeFaceId = person.RepresentativeFaceId,
                ConfirmedFaceCount = person.ConfirmedFaceCount,
                PhotoCount = person.PhotoCount,
                LatestMediaDateUtc = person.LatestMediaDateUtc,
                IsHidden = person.IsHidden,
                IsMinor = person.IsMinor,
                ConcurrencyToken = person.ConcurrencyToken
            });
    }

    internal static IQueryable<MediaFace> BuildReviewableFacesQuery(MediaLibraryDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.Faces
            .AsNoTracking()
            .Where(face => !face.IsSuppressed
                           && face.QualityStatus != FaceQualityStatus.ProcessingFailed
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && !db.PersonFaces.Any(assignment =>
                               assignment.MediaFaceId == face.Id
                               && assignment.RemovedAtUtc == null)
                           && !db.FaceReviewDecisions.Any(decision =>
                               decision.MediaFaceId == face.Id
                               && !decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Ignored));
    }

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
                candidate.RepresentativeFaceId,
                candidate.Similarity,
                candidate.ConcurrencyToken,
                index + 1,
                margin,
                isStrong,
                isAmbiguous));
        }

        return results;
    }

    private static string IdentityActionLabel(string action)
        => action switch
        {
            "PersonCreated" => "Person created",
            "PersonGroupCreated" => "Person created from appearances",
            "FaceAssigned" => "Appearance confirmed",
            "FaceReassigned" => "Appearance reassigned",
            "FaceGroupAssigned" => "Appearances confirmed",
            "AssignmentRemoved" => "Appearance returned to review",
            "AssignmentMoved" => "Appearance moved",
            "AppearancesMoved" => "Appearances moved",
            "PersonSplit" => "New person created from selected appearances",
            "PeopleMerged" => "People merged",
            "AssignmentMerged" => "Appearance merged",
            "PersonRenamed" => "Person renamed",
            "PersonHidden" => "Person hidden",
            "PersonRestored" => "Person restored",
            "RepresentativeFaceChanged" => "Cover appearance changed",
            "FaceSuppressed" => "Invalid face detection removed",
            "FaceLeftUnidentified" => "Face left unidentified",
            "CandidateRejected" => "Identity suggestion rejected",
            "GroupCandidateRejected" => "Group identity suggestion rejected",
            _ => action
        };

    private static string NormalizeSort(string? sort)
        => sort?.Trim().ToLowerInvariant() switch
        {
            "photos" => "photos",
            "recent" => "recent",
            _ => "name"
        };

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record ReviewCandidateRow(
        Guid FaceId,
        long DecisionId,
        Guid PersonId,
        string DisplayName,
        Guid? RepresentativeFaceId,
        double? Similarity,
        Guid ConcurrencyToken);
}

internal sealed class MediaPersonIndexDatabaseRow
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public Guid? RepresentativeFaceId { get; init; }
    public int ConfirmedFaceCount { get; init; }
    public int PhotoCount { get; init; }
    public DateTimeOffset? LatestMediaDateUtc { get; init; }
    public bool IsHidden { get; init; }
    public bool IsMinor { get; init; }
    public Guid ConcurrencyToken { get; init; }
}

internal sealed class MediaPersonPhotoDatabaseRow
{
    public long AssetId { get; init; }
    public Guid FaceId { get; init; }
    public string ContextTitle { get; init; } = string.Empty;
    public string ContextSubtitle { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public DateTimeOffset MediaDateUtc { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double FaceQualityScore { get; init; }
}

internal sealed class ReviewFaceDatabaseRow
{
    public Guid FaceId { get; init; }
    public long AssetId { get; init; }
    public string ContextTitle { get; init; } = string.Empty;
    public string ContextSubtitle { get; init; } = string.Empty;
    public DateTimeOffset MediaDateUtc { get; init; }
    public double QualityScore { get; init; }
}

internal sealed class ReviewCandidateDatabaseRow
{
    public Guid FaceId { get; init; }
    public long DecisionId { get; init; }
    public Guid PersonId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public Guid? RepresentativeFaceId { get; init; }
    public double? Similarity { get; init; }
    public Guid ConcurrencyToken { get; init; }
}
