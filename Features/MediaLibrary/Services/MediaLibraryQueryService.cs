using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Read-optimised facade for the Photos experience. The timeline is the critical query;
/// optional facets are isolated so their failure cannot make core PRISM photos unavailable.
/// </summary>
public sealed class MediaLibraryQueryService : IMediaLibraryQueryService
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IMediaLibraryDiagnostics _diagnostics;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly ILogger<MediaLibraryQueryService> _logger;

    public MediaLibraryQueryService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IMediaLibraryDiagnostics diagnostics,
        IMediaAssetVisibilityPolicy visibility,
        ILogger<MediaLibraryQueryService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaLibraryQueryResult> SearchAsync(
        MediaLibraryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.IsCatalogueEnabled)
        {
            return MediaLibraryQueryResult.Unavailable(
                request.PageNumber,
                request.PageSize,
                "The media catalogue is disabled. PRISM-owned media will be shown directly.");
        }

        var warnings = new List<MediaLibraryQueryWarning>();
        IQueryable<MediaAsset> filteredQuery;
        IQueryable<MediaAsset> queryWithoutYear;
        List<MediaLibraryQueryItem> items;
        int total;
        int pageSize;
        int pageNumber;
        int skip;

        var primaryStopwatch = Stopwatch.StartNew();
        try
        {
            var baseQuery = BuildBaseQuery();
            baseQuery = ApplySource(baseQuery, request.Source);
            baseQuery = ApplyKind(baseQuery, request.Kind);
            baseQuery = ApplyClassification(baseQuery, request.Classification);

            if (request.ProjectId.HasValue)
            {
                baseQuery = baseQuery.Where(asset => asset.ProjectId == request.ProjectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.CollectionKey))
            {
                var collectionKey = request.CollectionKey.Trim();
                baseQuery = baseQuery.Where(asset => asset.CollectionKey == collectionKey);
            }

            if (request.AssetIds is not null)
            {
                var requestedAssetIds = request.AssetIds
                    .Where(id => id > 0)
                    .Distinct()
                    .Take(500)
                    .ToArray();
                baseQuery = requestedAssetIds.Length == 0
                    ? baseQuery.Where(_ => false)
                    : baseQuery.Where(asset => requestedAssetIds.Contains(asset.Id));
            }

            var selectedPersonIds = NormalizeSelectedPeople(request);
            if (request.IncludePeople && selectedPersonIds.Count > 0)
            {
                baseQuery = ApplyPeopleFilter(
                    baseQuery,
                    selectedPersonIds,
                    request.PeopleMatch);
            }

            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                var pattern = $"%{EscapeLikePattern(request.Query.Trim())}%";
                var metadataMatches = baseQuery.Where(asset =>
                    EF.Functions.ILike(asset.Title, pattern, "\\")
                    || (asset.Caption != null && EF.Functions.ILike(asset.Caption, pattern, "\\"))
                    || (asset.EditorialCaption != null && EF.Functions.ILike(asset.EditorialCaption, pattern, "\\"))
                    || EF.Functions.ILike(asset.ContextTitle, pattern, "\\")
                    || EF.Functions.ILike(asset.ContextSubtitle, pattern, "\\")
                    || EF.Functions.ILike(asset.OriginalFileName, pattern, "\\")
                    || EF.Functions.ILike(asset.SourceLabel, pattern, "\\")
                    || EF.Functions.ILike(asset.Source.Name, pattern, "\\")
                    || (asset.RelativePath != null && EF.Functions.ILike(asset.RelativePath, pattern, "\\"))
                    || asset.AlbumItems.Any(item => !item.MediaAlbum.IsArchived
                        && EF.Functions.ILike(item.MediaAlbum.Name, pattern, "\\")));

                baseQuery = request.IncludePeople
                    ? baseQuery.Where(asset =>
                        EF.Functions.ILike(asset.Title, pattern, "\\")
                        || (asset.Caption != null && EF.Functions.ILike(asset.Caption, pattern, "\\"))
                        || (asset.EditorialCaption != null && EF.Functions.ILike(asset.EditorialCaption, pattern, "\\"))
                        || EF.Functions.ILike(asset.ContextTitle, pattern, "\\")
                        || EF.Functions.ILike(asset.ContextSubtitle, pattern, "\\")
                        || EF.Functions.ILike(asset.OriginalFileName, pattern, "\\")
                        || EF.Functions.ILike(asset.SourceLabel, pattern, "\\")
                        || EF.Functions.ILike(asset.Source.Name, pattern, "\\")
                        || (asset.RelativePath != null && EF.Functions.ILike(asset.RelativePath, pattern, "\\"))
                        || asset.AlbumItems.Any(item => !item.MediaAlbum.IsArchived
                            && EF.Functions.ILike(item.MediaAlbum.Name, pattern, "\\"))
                        || asset.Faces.Any(face => face.PersonAssignments.Any(assignment =>
                            assignment.RemovedAtUtc == null
                            && assignment.MediaPerson.Status == MediaPersonStatus.Confirmed
                            && !assignment.MediaPerson.IsHidden
                            && EF.Functions.ILike(assignment.MediaPerson.DisplayName, pattern, "\\"))))
                    : metadataMatches;
            }

            queryWithoutYear = baseQuery;
            filteredQuery = request.Year.HasValue
                ? baseQuery.Where(asset => asset.MediaDateUtc.Year == request.Year.Value)
                : baseQuery;
            total = await filteredQuery.CountAsync(cancellationToken);
            pageSize = Math.Clamp(request.PageSize, 1, 250);
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            pageNumber = Math.Clamp(request.PageNumber, 1, pageCount);
            skip = (pageNumber - 1) * pageSize;

            var orderedQuery = string.Equals(request.Sort?.Trim(), "oldest", StringComparison.OrdinalIgnoreCase)
                ? filteredQuery.OrderBy(asset => asset.MediaDateUtc)
                    .ThenBy(asset => asset.ContextTitle)
                    .ThenBy(asset => asset.SortOrder)
                    .ThenBy(asset => asset.Id)
                : filteredQuery.OrderByDescending(asset => asset.MediaDateUtc)
                    .ThenBy(asset => asset.ContextTitle)
                    .ThenBy(asset => asset.SortOrder)
                    .ThenBy(asset => asset.Id);

            var rows = await orderedQuery
                .Skip(skip)
                .Take(pageSize)
                .Select(asset => new TimelineRow(
                    asset.Id,
                    asset.SourceId,
                    asset.Origin,
                    asset.Kind,
                    asset.Classification,
                    asset.SourceEntityId,
                    asset.ParentEntityId,
                    asset.ContextKey,
                    asset.CollectionKey,
                    asset.ContextTitle,
                    asset.ContextSubtitle,
                    asset.SourceLabel,
                    asset.Title,
                    asset.Caption,
                    asset.EditorialCaption,
                    asset.EditorialConcurrencyToken,
                    asset.OriginalFileName,
                    asset.FileSizeBytes,
                    asset.MediaDateUtc,
                    asset.Width,
                    asset.Height,
                    asset.DurationSeconds,
                    asset.IsCover,
                    asset.SortOrder,
                    asset.CacheVersion,
                    asset.VersionToken))
                .ToListAsync(cancellationToken);

            var assetIds = rows.Select(row => row.Id).ToArray();
            var peopleByAsset = new Dictionary<long, IReadOnlyList<MediaLibraryPersonSummary>>();
            var unidentifiedByAsset = new Dictionary<long, int>();
            if (request.IncludePeople && _options.People.Enabled && assetIds.Length > 0)
            {
                var assignments = await _db.PersonFaces
                    .AsNoTracking()
                    .Where(assignment => assignment.RemovedAtUtc == null
                                         && !assignment.MediaFace.IsSuppressed
                                         && assetIds.Contains(assignment.MediaFace.MediaAssetId)
                                         && !assignment.MediaPerson.IsHidden
                                         && assignment.MediaPerson.Status == MediaPersonStatus.Confirmed)
                    .Select(assignment => new
                    {
                        AssetId = assignment.MediaFace.MediaAssetId,
                        PersonId = assignment.MediaPersonId,
                        assignment.MediaPerson.DisplayName
                    })
                    .Distinct()
                    .OrderBy(item => item.DisplayName)
                    .ToListAsync(cancellationToken);
                peopleByAsset = assignments
                    .GroupBy(item => item.AssetId)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<MediaLibraryPersonSummary>)group
                            .Select(item => new MediaLibraryPersonSummary(item.PersonId, item.DisplayName))
                            .ToList());

                if (request.IncludeUnidentifiedFaces)
                {
                    unidentifiedByAsset = await _db.Faces
                        .AsNoTracking()
                        .Where(face => assetIds.Contains(face.MediaAssetId)
                                       && !face.IsSuppressed
                                       && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null))
                        .GroupBy(face => face.MediaAssetId)
                        .Select(group => new { AssetId = group.Key, Count = group.Count() })
                        .ToDictionaryAsync(item => item.AssetId, item => item.Count, cancellationToken);
                }
            }

            var albumsByAsset = new Dictionary<long, IReadOnlyList<MediaLibraryAlbumSummary>>();
            if (assetIds.Length > 0)
            {
                var albumRows = await _db.AlbumItems
                    .AsNoTracking()
                    .Where(item => assetIds.Contains(item.MediaAssetId) && !item.MediaAlbum.IsArchived)
                    .Select(item => new
                    {
                        item.MediaAssetId,
                        AlbumId = item.MediaAlbumId,
                        item.MediaAlbum.Name
                    })
                    .OrderBy(item => item.Name)
                    .ThenBy(item => item.AlbumId)
                    .ToListAsync(cancellationToken);

                albumsByAsset = albumRows
                    .GroupBy(item => item.MediaAssetId)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<MediaLibraryAlbumSummary>)group
                            .Select(item => new MediaLibraryAlbumSummary(item.AlbumId, item.Name))
                            .ToList());
            }

            items = rows.Select(row => new MediaLibraryQueryItem(
                row.Id,
                row.SourceId,
                row.Origin,
                row.Kind,
                row.Classification,
                row.SourceEntityId,
                row.ParentEntityId,
                row.ContextKey,
                row.CollectionKey,
                row.ContextTitle,
                row.ContextSubtitle,
                row.SourceLabel,
                row.Title,
                row.Caption,
                row.EditorialCaption,
                row.EditorialConcurrencyToken,
                row.OriginalFileName,
                row.FileSizeBytes,
                row.MediaDateUtc,
                row.Width,
                row.Height,
                row.DurationSeconds,
                row.IsCover,
                row.SortOrder,
                row.CacheVersion,
                row.VersionToken,
                peopleByAsset.GetValueOrDefault(row.Id) ?? Array.Empty<MediaLibraryPersonSummary>(),
                unidentifiedByAsset.GetValueOrDefault(row.Id),
                albumsByAsset.GetValueOrDefault(row.Id) ?? Array.Empty<MediaLibraryAlbumSummary>()))
                .ToList();

            primaryStopwatch.Stop();
            _diagnostics.RecordSuccess(
                MediaLibraryQueryOperation.PrimaryTimeline,
                primaryStopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            primaryStopwatch.Stop();
            var diagnostic = _diagnostics.RecordFailure(
                MediaLibraryQueryOperation.PrimaryTimeline,
                exception,
                primaryStopwatch.ElapsedMilliseconds);
            _logger.LogWarning(
                exception,
                "Primary media catalogue query failed. Reference {Reference}.",
                diagnostic.Reference);
            return MediaLibraryQueryResult.Unavailable(
                request.PageNumber,
                request.PageSize,
                $"The unified media timeline is temporarily unavailable. Reference {diagnostic.Reference}.",
                ToWarning(diagnostic));
        }

        var statistics = await ExecuteOptionalAsync(
            MediaLibraryQueryOperation.Statistics,
            async () =>
            {
                var counts = await filteredQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Total = group.Count(),
                        Photos = group.Count(asset => asset.Kind == MediaAssetKind.Photo),
                        Videos = group.Count(asset => asset.Kind == MediaAssetKind.Video)
                    })
                    .FirstOrDefaultAsync(cancellationToken);
                var collections = await filteredQuery
                    .Select(asset => asset.CollectionKey)
                    .Distinct()
                    .CountAsync(cancellationToken);
                return new MediaLibraryStatistics(
                    counts?.Total ?? 0,
                    counts?.Photos ?? 0,
                    counts?.Videos ?? 0,
                    collections);
            },
            new MediaLibraryStatistics(
                total,
                items.Count(item => item.Kind == MediaAssetKind.Photo),
                items.Count(item => item.Kind == MediaAssetKind.Video),
                items.Select(item => item.CollectionKey).Distinct(StringComparer.Ordinal).Count()),
            warnings,
            cancellationToken);

        var years = await ExecuteOptionalAsync<IReadOnlyList<int>>(
            MediaLibraryQueryOperation.Years,
            async () => await queryWithoutYear
                .Select(asset => asset.MediaDateUtc.Year)
                .Distinct()
                .OrderByDescending(year => year)
                .ToListAsync(cancellationToken),
            Array.Empty<int>(),
            warnings,
            cancellationToken);

        var projects = await ExecuteOptionalAsync<IReadOnlyList<MediaLibraryProjectOption>>(
            MediaLibraryQueryOperation.Projects,
            async () =>
            {
                var projectRows = await BuildBaseQuery()
                    .Where(asset => asset.ProjectId.HasValue)
                    .Select(asset => new { Id = asset.ProjectId!.Value, Name = asset.ContextTitle })
                    .Distinct()
                    .OrderBy(project => project.Name)
                    .ThenBy(project => project.Id)
                    .ToListAsync(cancellationToken);
                return projectRows
                    .Select(row => new MediaLibraryProjectOption(row.Id, row.Name))
                    .ToList();
            },
            Array.Empty<MediaLibraryProjectOption>(),
            warnings,
            cancellationToken);

        var visiblePeopleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        IReadOnlyList<MediaLibraryPersonOption> people = request.IncludePeople && _options.People.Enabled
            ? await ExecuteOptionalAsync<IReadOnlyList<MediaLibraryPersonOption>>(
                MediaLibraryQueryOperation.People,
                async () => await _db.Persons
                    .AsNoTracking()
                    .Where(person => person.Status == MediaPersonStatus.Confirmed && !person.IsHidden)
                    .OrderBy(person => person.DisplayName)
                    .Select(person => new MediaLibraryPersonOption(
                        person.Id,
                        person.DisplayName,
                        person.FaceAssignments
                            .Where(assignment => assignment.RemovedAtUtc == null
                                                 && visiblePeopleAssetIds.Contains(assignment.MediaFace.MediaAssetId))
                            .Select(assignment => assignment.MediaFace.MediaAssetId)
                            .Distinct()
                            .Count(),
                        person.RepresentativeFaceId))
                    .ToListAsync(cancellationToken),
                Array.Empty<MediaLibraryPersonOption>(),
                warnings,
                cancellationToken)
            : Array.Empty<MediaLibraryPersonOption>();

        var hasPrismCatalogue = await ExecuteOptionalAsync(
            MediaLibraryQueryOperation.PrismSourceStatus,
            async () => await _db.Sources
                .AsNoTracking()
                .AnyAsync(source => source.Key == MediaSourceBootstrapper.PrismSourceKey
                                    && !source.IsDeleted
                                    && source.IsEnabled
                                    && source.LastSuccessfulScanAtUtc.HasValue
                                    && source.ScanStatus == "Healthy",
                    cancellationToken),
            true,
            warnings,
            cancellationToken);

        var summaryWarning = warnings.Count == 0
            ? null
            : $"The media timeline is available, but {warnings.Count} optional catalogue feature(s) are degraded. Reference {warnings[0].Reference}.";
        return new MediaLibraryQueryResult(
            items,
            projects,
            people,
            years,
            statistics,
            pageNumber,
            pageSize,
            pageNumber > 1,
            total > skip + pageSize,
            true,
            true,
            hasPrismCatalogue,
            warnings,
            summaryWarning);
    }

    private IQueryable<MediaAsset> BuildBaseQuery()
        => _visibility.Apply(_db.Assets.AsNoTracking());

    private async Task<T> ExecuteOptionalAsync<T>(
        MediaLibraryQueryOperation operation,
        Func<Task<T>> action,
        T fallback,
        ICollection<MediaLibraryQueryWarning> warnings,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await action();
            stopwatch.Stop();
            _diagnostics.RecordSuccess(operation, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var diagnostic = _diagnostics.RecordFailure(operation, exception, stopwatch.ElapsedMilliseconds);
            warnings.Add(ToWarning(diagnostic));
            _logger.LogWarning(
                exception,
                "Optional media catalogue query {Operation} failed. Reference {Reference}. The timeline remains available.",
                operation,
                diagnostic.Reference);
            return fallback;
        }
    }

    private static MediaLibraryQueryWarning ToWarning(MediaLibraryDiagnosticEvent diagnostic)
        => new(
            diagnostic.Operation,
            diagnostic.Reference,
            diagnostic.Message,
            diagnostic.OccurredAtUtc);

    private static IReadOnlyList<Guid> NormalizeSelectedPeople(MediaLibraryQuery request)
    {
        var selected = new List<Guid>(capacity: 10);

        if (request.PersonId.HasValue && request.PersonId.Value != Guid.Empty)
        {
            selected.Add(request.PersonId.Value);
        }

        if (request.PersonIds is not null)
        {
            foreach (var personId in request.PersonIds)
            {
                if (personId == Guid.Empty || selected.Contains(personId))
                {
                    continue;
                }

                selected.Add(personId);
                if (selected.Count == 10)
                {
                    break;
                }
            }
        }

        return selected;
    }

    internal static IQueryable<MediaAsset> ApplyPeopleFilter(
        IQueryable<MediaAsset> query,
        IReadOnlyList<Guid> personIds,
        string? matchMode)
    {
        if (personIds.Count == 0)
        {
            return query;
        }

        var matchAll = !string.Equals(
            matchMode?.Trim(),
            "any",
            StringComparison.OrdinalIgnoreCase);

        if (!matchAll)
        {
            var selectedIds = personIds.ToArray();
            return query.Where(asset => asset.Faces.Any(face =>
                !face.IsSuppressed
                && face.PersonAssignments.Any(assignment =>
                    assignment.RemovedAtUtc == null
                    && selectedIds.Contains(assignment.MediaPersonId)
                    && assignment.MediaPerson.Status == MediaPersonStatus.Confirmed
                    && !assignment.MediaPerson.IsHidden)));
        }

        // Build one correlated EXISTS predicate per person. This produces dependable
        // PostgreSQL translation and exact "all selected people appear" semantics.
        foreach (var personId in personIds)
        {
            var requiredPersonId = personId;
            query = query.Where(asset => asset.Faces.Any(face =>
                !face.IsSuppressed
                && face.PersonAssignments.Any(assignment =>
                    assignment.RemovedAtUtc == null
                    && assignment.MediaPersonId == requiredPersonId
                    && assignment.MediaPerson.Status == MediaPersonStatus.Confirmed
                    && !assignment.MediaPerson.IsHidden)));
        }

        return query;
    }

    private static IQueryable<MediaAsset> ApplySource(IQueryable<MediaAsset> query, string source)
        => source switch
        {
            "projects" => query.Where(asset => asset.Origin == MediaAssetOrigin.ProjectPhoto
                                               || asset.Origin == MediaAssetOrigin.ProjectVideo),
            "visits" => query.Where(asset => asset.Origin == MediaAssetOrigin.VisitPhoto),
            "events" => query.Where(asset => asset.Origin == MediaAssetOrigin.SocialMediaEventPhoto),
            "activities" => query.Where(asset => asset.Origin == MediaAssetOrigin.ActivityPhoto),
            "external" => query.Where(asset => asset.Origin == MediaAssetOrigin.ExternalFile),
            _ => query
        };

    private static IQueryable<MediaAsset> ApplyKind(IQueryable<MediaAsset> query, string kind)
        => kind switch
        {
            "photo" => query.Where(asset => asset.Kind == MediaAssetKind.Photo),
            "video" => query.Where(asset => asset.Kind == MediaAssetKind.Video),
            _ => query
        };

    private static IQueryable<MediaAsset> ApplyClassification(
        IQueryable<MediaAsset> query,
        string classification)
        => classification switch
        {
            "photograph" => query.Where(asset => asset.Classification == MediaClassification.Photograph),
            "screenshot" => query.Where(asset => asset.Classification == MediaClassification.Screenshot),
            "scanned-document" => query.Where(asset => asset.Classification == MediaClassification.ScannedDocument),
            "diagram" => query.Where(asset => asset.Classification == MediaClassification.Diagram),
            "presentation-slide" => query.Where(asset => asset.Classification == MediaClassification.PresentationSlide),
            "graphic" => query.Where(asset => asset.Classification == MediaClassification.Graphic),
            "unknown" => query.Where(asset => asset.Classification == MediaClassification.Unknown),
            _ => query
        };

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record TimelineRow(
        long Id,
        Guid SourceId,
        MediaAssetOrigin Origin,
        MediaAssetKind Kind,
        MediaClassification Classification,
        string SourceEntityId,
        string? ParentEntityId,
        string ContextKey,
        string CollectionKey,
        string ContextTitle,
        string ContextSubtitle,
        string SourceLabel,
        string Title,
        string? Caption,
        string? EditorialCaption,
        Guid EditorialConcurrencyToken,
        string OriginalFileName,
        long FileSizeBytes,
        DateTimeOffset MediaDateUtc,
        int? Width,
        int? Height,
        int? DurationSeconds,
        bool IsCover,
        long SortOrder,
        int CacheVersion,
        string? VersionToken);
}
