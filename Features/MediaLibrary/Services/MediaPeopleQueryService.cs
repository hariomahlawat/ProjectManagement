using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaPeopleQueryService : IMediaPeopleQueryService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly MediaPeopleOptions _options;

    public MediaPeopleQueryService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
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
            request.IncludeHidden,
            request.AccountLinkFilter);
        var total = await filteredPeople.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var pageNumber = Math.Clamp(request.PageNumber, 1, pageCount);

        // Aggregation, ordering and pagination all remain on scalar SQL columns. The former
        // query projected a MediaPersonCard containing correlated subqueries and then ordered
        // the CLR record; Npgsql correctly rejected that non-translatable expression tree.
        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);
        var databaseRows = await BuildPeopleIndexRowsQuery(
                _db,
                filteredPeople,
                normalizedSort,
                (pageNumber - 1) * pageSize,
                pageSize,
                visibleAssetIds)
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
                row.ConcurrencyToken,
                row.IsPrismUserLinked,
                row.HasOpenPrismLinkConcern))
            .ToList();

        var reviewableFaces = BuildVisibleReviewableFacesQuery();
        var activePendingKnownPerson = FaceReviewWorkloadService.BuildActivePendingKnownPersonDecisions(_db);
        var totalUnassignedFaceCount = await reviewableFaces.CountAsync(cancellationToken);
        var knownPersonSuggestionCount = await reviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
            && activePendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id),
            cancellationToken);
        var candidateSearchPendingCount = await reviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Pending
            || face.CandidateSearchStatus == FaceCandidateSearchStatus.Processing,
            cancellationToken);
        var candidateSearchFailureCount = await reviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Failed,
            cancellationToken);
        var unidentifiedFaceCount = await reviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus != FaceCandidateSearchStatus.Pending
            && face.CandidateSearchStatus != FaceCandidateSearchStatus.Processing
            && !(face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
                 && activePendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id)),
            cancellationToken);
        var pendingReviewCount = knownPersonSuggestionCount + unidentifiedFaceCount;

        return new MediaPeopleIndexResult(
            people,
            total,
            pendingReviewCount,
            unidentifiedFaceCount,
            pageNumber,
            pageSize,
            pageNumber > 1,
            pageNumber < pageCount,
            knownPersonSuggestionCount,
            candidateSearchPendingCount,
            candidateSearchFailureCount,
            totalUnassignedFaceCount);
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

        var visiblePersonAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        var assignmentRows = await (
                from assignment in _db.PersonFaces.AsNoTracking()
                join face in _db.Faces.AsNoTracking()
                    on assignment.MediaFaceId equals face.Id
                join asset in _db.Assets.AsNoTracking()
                    on face.MediaAssetId equals asset.Id
                where assignment.MediaPersonId == personId
                      && assignment.RemovedAtUtc == null
                      && !face.IsSuppressed
                      && visiblePersonAssetIds.Contains(asset.Id)
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
                    FaceQualityScore = face.QualityScore,
                    ReferenceStatus = assignment.ReferenceStatus,
                    AssignmentType = assignment.AssignmentType,
                    AssignmentConfidence = assignment.AssignmentConfidence,
                    FaceLeft = face.Left,
                    FaceTop = face.Top,
                    FaceWidth = face.Width,
                    FaceHeight = face.Height
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
                person.RepresentativeFaceId == row.FaceId,
                row.ReferenceStatus,
                row.AssignmentType,
                row.AssignmentConfidence,
                row.FaceLeft,
                row.FaceTop,
                row.FaceWidth,
                row.FaceHeight))
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
            assignments.Count(item => item.ReferenceStatus == FaceReferenceStatus.TrustedReference),
            assignments.Count == 0 ? null : assignments.Min(item => item.MediaDateUtc),
            assignments.Count == 0 ? null : assignments.Max(item => item.MediaDateUtc),
            assignments,
            mergeTargets,
            history);
    }

    public Task<FaceReviewQueueResult> GetReviewQueueAsync(
        FaceReviewQueueKind kind,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
        => GetReviewQueueAsync(
            new FaceReviewQueueQuery(kind, pageNumber, pageSize),
            cancellationToken);

    public async Task<FaceReviewQueueResult> GetReviewQueueAsync(
        FaceReviewQueueQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = request.Kind;
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 12, 100);
        var allReviewableFaces = kind == FaceReviewQueueKind.ClosedUnidentified
            ? BuildVisibleClosedUnidentifiedFacesQuery()
            : BuildVisibleReviewableFacesQuery();
        var assetIds = (request.AssetIds ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .Take(250)
            .ToArray();
        if (assetIds.Length > 0)
        {
            allReviewableFaces = allReviewableFaces.Where(face => assetIds.Contains(face.MediaAssetId));
        }

        allReviewableFaces = ApplyReviewSourceFilter(allReviewableFaces, request.Source);
        var availableYears = await allReviewableFaces
            .Select(face => face.MediaAsset.MediaDateUtc.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        if (request.Year.HasValue)
        {
            allReviewableFaces = allReviewableFaces
                .Where(face => face.MediaAsset.MediaDateUtc.Year == request.Year.Value);
        }

        allReviewableFaces = ApplyReviewMatchStatusFilter(allReviewableFaces, request.MatchStatus);

        var activePendingKnownPerson = FaceReviewWorkloadService.BuildActivePendingKnownPersonDecisions(_db);
        var knownMatchCount = await allReviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
            && activePendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id),
            cancellationToken);
        var unidentifiedCount = await allReviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus != FaceCandidateSearchStatus.Pending
            && face.CandidateSearchStatus != FaceCandidateSearchStatus.Processing
            && !(face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
                 && activePendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id)),
            cancellationToken);
        var candidateSearchPendingCount = await allReviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Pending
            || face.CandidateSearchStatus == FaceCandidateSearchStatus.Processing,
            cancellationToken);
        var candidateSearchFailureCount = await allReviewableFaces.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Failed,
            cancellationToken);

        var reviewableFaces = kind switch
        {
            FaceReviewQueueKind.KnownMatches => allReviewableFaces.Where(face =>
                face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
                && activePendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id)),
            FaceReviewQueueKind.ClosedUnidentified => allReviewableFaces,
            _ => allReviewableFaces.Where(face =>
                face.CandidateSearchStatus != FaceCandidateSearchStatus.Pending
                && face.CandidateSearchStatus != FaceCandidateSearchStatus.Processing
                && !(face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
                     && activePendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id)))
        };

        var totalFaces = await reviewableFaces.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalFaces / (double)pageSize));
        pageNumber = Math.Clamp(pageNumber, 1, pageCount);
        var orderedFaces = request.Sort?.Trim().ToLowerInvariant() switch
        {
            "quality-asc" => reviewableFaces
                .OrderBy(face => face.QualityScore)
                .ThenByDescending(face => face.CreatedAtUtc),
            "newest" => reviewableFaces
                .OrderByDescending(face => face.MediaAsset.MediaDateUtc)
                .ThenByDescending(face => face.QualityScore),
            "oldest" => reviewableFaces
                .OrderBy(face => face.MediaAsset.MediaDateUtc)
                .ThenByDescending(face => face.QualityScore),
            _ => reviewableFaces
                .OrderByDescending(face => face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
                    && activePendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id))
                .ThenByDescending(face => face.QualityScore)
                .ThenByDescending(face => face.CreatedAtUtc)
        };

        var faceRows = await orderedFaces
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(face => new ReviewFaceDatabaseRow
            {
                FaceId = face.Id,
                AssetId = face.MediaAssetId,
                ContextTitle = face.MediaAsset.ContextTitle,
                ContextSubtitle = face.MediaAsset.ContextSubtitle,
                MediaDateUtc = face.MediaAsset.MediaDateUtc,
                QualityScore = face.QualityScore,
                CandidateSearchStatus = face.CandidateSearchStatus,
                CandidateSearchFailureReason = face.CandidateSearchFailureReason
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
                          && decision.MediaFace.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
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
                        BestReferenceSimilarity = decision.BestReferenceSimilarity,
                        MeanTopSimilarity = decision.MeanTopSimilarity,
                        ReferenceCount = decision.ReferenceCount,
                        MarginToNext = decision.MarginToNext,
                        MarginAvailable = decision.MarginAvailable,
                        ConfidenceLevel = decision.ConfidenceLevel,
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
                        candidate.BestReferenceSimilarity,
                        candidate.MeanTopSimilarity,
                        candidate.ReferenceCount,
                        candidate.MarginToNext,
                        candidate.MarginAvailable,
                        candidate.ConfidenceLevel,
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
                    ?? Array.Empty<FaceReviewCandidateItem>(),
                face.CandidateSearchStatus,
                face.CandidateSearchFailureReason))
            .ToList();

        var availablePeople = await GetPersonOptionsAsync(cancellationToken);
        return new FaceReviewQueueResult(
            items,
            availablePeople,
            totalFaces,
            pageNumber,
            pageSize,
            pageNumber > 1,
            pageNumber < pageCount,
            knownMatchCount,
            unidentifiedCount,
            candidateSearchPendingCount,
            candidateSearchFailureCount,
            availableYears);
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
        bool includeHidden,
        string accountLinkFilter = "all")
    {
        ArgumentNullException.ThrowIfNull(db);

        var peopleQuery = db.Persons.AsNoTracking();
        peopleQuery = includeHidden
            ? peopleQuery.Where(person => person.Status == MediaPersonStatus.Confirmed
                                          || person.Status == MediaPersonStatus.Hidden)
            : peopleQuery.Where(person => person.Status == MediaPersonStatus.Confirmed
                                          && !person.IsHidden);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var escapedTerm = EscapeLikePattern(query.Trim());
            peopleQuery = peopleQuery.Where(person =>
                EF.Functions.ILike(person.DisplayName, $"%{escapedTerm}%", "\\"));
        }

        var normalizedLinkFilter = NormalizeAccountLinkFilter(accountLinkFilter);
        if (normalizedLinkFilter != "all")
        {
            var activeLinks = db.PersonUserLinks.AsNoTracking()
                .Where(link => link.UnlinkedAtUtc == null);
            peopleQuery = normalizedLinkFilter switch
            {
                "linked" => peopleQuery.Where(person =>
                    activeLinks.Any(link => link.MediaPersonId == person.Id)),
                "unlinked" => peopleQuery.Where(person =>
                    !activeLinks.Any(link => link.MediaPersonId == person.Id)),
                "reported" => peopleQuery.Where(person =>
                    activeLinks.Any(link => link.MediaPersonId == person.Id
                                            && link.ConcernRaisedAtUtc != null
                                            && link.ConcernResolvedAtUtc == null)),
                _ => peopleQuery
            };
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
        int take,
        IQueryable<long>? visibleAssetIds = null)
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

        if (visibleAssetIds is not null)
        {
            availableAssignmentRows = availableAssignmentRows
                .Where(row => visibleAssetIds.Contains(row.AssetId));
        }

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
                person.ConcurrencyToken,
                IsPrismUserLinked = db.PersonUserLinks.Any(link =>
                    link.MediaPersonId == person.Id && link.UnlinkedAtUtc == null),
                HasOpenPrismLinkConcern = db.PersonUserLinks.Any(link =>
                    link.MediaPersonId == person.Id
                    && link.UnlinkedAtUtc == null
                    && link.ConcernRaisedAtUtc != null
                    && link.ConcernResolvedAtUtc == null)
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
                ConcurrencyToken = person.ConcurrencyToken,
                IsPrismUserLinked = person.IsPrismUserLinked,
                HasOpenPrismLinkConcern = person.HasOpenPrismLinkConcern
            });
    }

    private static IQueryable<MediaFace> ApplyReviewSourceFilter(
        IQueryable<MediaFace> query,
        string? source)
        => source?.Trim().ToLowerInvariant() switch
        {
            "projects" => query.Where(face =>
                face.MediaAsset.Origin == MediaAssetOrigin.ProjectPhoto
                || face.MediaAsset.Origin == MediaAssetOrigin.ProjectVideo),
            "visits" => query.Where(face => face.MediaAsset.Origin == MediaAssetOrigin.VisitPhoto),
            "events" => query.Where(face => face.MediaAsset.Origin == MediaAssetOrigin.SocialMediaEventPhoto),
            "activities" => query.Where(face => face.MediaAsset.Origin == MediaAssetOrigin.ActivityPhoto),
            "external" => query.Where(face => face.MediaAsset.Origin == MediaAssetOrigin.ExternalFile),
            _ => query
        };

    private static IQueryable<MediaFace> ApplyReviewMatchStatusFilter(
        IQueryable<MediaFace> query,
        string? matchStatus)
        => matchStatus?.Trim().ToLowerInvariant() switch
        {
            "no-match" => query.Where(face => face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready),
            "failed" => query.Where(face => face.CandidateSearchStatus == FaceCandidateSearchStatus.Failed),
            "not-requested" => query.Where(face => face.CandidateSearchStatus == FaceCandidateSearchStatus.NotRequested),
            _ => query
        };

    private IQueryable<MediaFace> BuildVisibleReviewableFacesQuery()
    {
        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        return BuildReviewableFacesQuery(_db)
            .Where(face => visibleAssetIds.Contains(face.MediaAssetId));
    }

    private IQueryable<MediaFace> BuildVisibleClosedUnidentifiedFacesQuery()
    {
        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        return FaceReviewWorkloadService.BuildClosedUnidentifiedFacesQuery(_db, visibleAssetIds);
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

    private static IReadOnlyList<FaceReviewCandidateItem> BuildCandidateItems(
        IReadOnlyList<ReviewCandidateRow> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.Similarity ?? double.NegativeInfinity)
            .ThenBy(candidate => candidate.DisplayName)
            .Select((candidate, index) => new FaceReviewCandidateItem(
                candidate.DecisionId,
                candidate.PersonId,
                candidate.DisplayName,
                candidate.RepresentativeFaceId,
                candidate.Similarity,
                candidate.ConcurrencyToken,
                index + 1,
                candidate.MarginToNext,
                candidate.MarginAvailable,
                candidate.ReferenceCount,
                candidate.BestReferenceSimilarity,
                candidate.MeanTopSimilarity,
                candidate.ConfidenceLevel,
                candidate.ConfidenceLevel == FaceCandidateConfidenceLevel.Strong,
                candidate.ConfidenceLevel == FaceCandidateConfidenceLevel.Possible))
            .ToList();
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
            "ReferenceTrusted" => "Appearance trusted for matching",
            "ReferencePreparationQueued" => "Matching evidence preparation queued",
            "ReferenceRemoved" => "Appearance removed from matching references",
            "ReferenceExcluded" => "Appearance excluded from matching",
            "FaceSuppressed" => "Invalid face detection removed",
            "FaceLeftUnidentified" => "Face closed as unidentified",
            "FaceUnidentifiedReopened" => "Face reopened for review",
            "CandidateRejected" => "Identity suggestion rejected",
            "GroupCandidateRejected" => "Group identity suggestion rejected",
            "PrismUserLinked" => "PRISM account linked",
            "PrismUserUnlinked" => "PRISM account unlinked",
            "PrismUserAvatarPreferenceChanged" => "PRISM avatar preference changed",
            "PrismUserLinkConcernRaised" => "PRISM account link reported",
            "PrismUserLinkConcernResolved" => "PRISM account link report resolved",
            _ => action
        };

    private static string NormalizeSort(string? sort)
        => sort?.Trim().ToLowerInvariant() switch
        {
            "photos" => "photos",
            "recent" => "recent",
            _ => "name"
        };

    internal static string NormalizeAccountLinkFilter(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "linked" => "linked",
            "unlinked" => "unlinked",
            "reported" => "reported",
            _ => "all"
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
        double? BestReferenceSimilarity,
        double? MeanTopSimilarity,
        int ReferenceCount,
        double? MarginToNext,
        bool MarginAvailable,
        FaceCandidateConfidenceLevel ConfidenceLevel,
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
    public bool IsPrismUserLinked { get; init; }
    public bool HasOpenPrismLinkConcern { get; init; }
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
    public FaceReferenceStatus ReferenceStatus { get; init; }
    public FaceAssignmentType AssignmentType { get; init; }
    public double? AssignmentConfidence { get; init; }
    public double FaceLeft { get; init; }
    public double FaceTop { get; init; }
    public double FaceWidth { get; init; }
    public double FaceHeight { get; init; }
}

internal sealed class ReviewFaceDatabaseRow
{
    public Guid FaceId { get; init; }
    public long AssetId { get; init; }
    public string ContextTitle { get; init; } = string.Empty;
    public string ContextSubtitle { get; init; } = string.Empty;
    public DateTimeOffset MediaDateUtc { get; init; }
    public double QualityScore { get; init; }
    public FaceCandidateSearchStatus CandidateSearchStatus { get; init; }
    public string? CandidateSearchFailureReason { get; init; }
}

internal sealed class ReviewCandidateDatabaseRow
{
    public Guid FaceId { get; init; }
    public long DecisionId { get; init; }
    public Guid PersonId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public Guid? RepresentativeFaceId { get; init; }
    public double? Similarity { get; init; }
    public double? BestReferenceSimilarity { get; init; }
    public double? MeanTopSimilarity { get; init; }
    public int ReferenceCount { get; init; }
    public double? MarginToNext { get; init; }
    public bool MarginAvailable { get; init; }
    public FaceCandidateConfidenceLevel ConfidenceLevel { get; init; }
    public Guid ConcurrencyToken { get; init; }
}
