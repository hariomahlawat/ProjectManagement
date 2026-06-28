using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Read-optimised facade for the Photos experience. The timeline is the critical query;
/// statistics and facets are deliberately isolated so an optional aggregate failure can
/// never force the complete Photos page back to the legacy read path.
/// </summary>
public sealed class MediaLibraryQueryService : IMediaLibraryQueryService
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IMediaLibraryDiagnostics _diagnostics;
    private readonly ILogger<MediaLibraryQueryService> _logger;

    public MediaLibraryQueryService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IMediaLibraryDiagnostics diagnostics,
        ILogger<MediaLibraryQueryService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                var pattern = $"%{EscapeLikePattern(request.Query.Trim())}%";
                baseQuery = baseQuery.Where(asset =>
                    EF.Functions.ILike(asset.Title, pattern, "\\")
                    || (asset.Caption != null && EF.Functions.ILike(asset.Caption, pattern, "\\"))
                    || EF.Functions.ILike(asset.ContextTitle, pattern, "\\")
                    || EF.Functions.ILike(asset.ContextSubtitle, pattern, "\\")
                    || EF.Functions.ILike(asset.OriginalFileName, pattern, "\\")
                    || EF.Functions.ILike(asset.SourceLabel, pattern, "\\")
                    || EF.Functions.ILike(asset.Source.Name, pattern, "\\")
                    || (asset.RelativePath != null && EF.Functions.ILike(asset.RelativePath, pattern, "\\")));
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

            items = await filteredQuery
                .OrderByDescending(asset => asset.MediaDateUtc)
                .ThenBy(asset => asset.ContextTitle)
                .ThenBy(asset => asset.SortOrder)
                .ThenBy(asset => asset.Id)
                .Skip(skip)
                .Take(pageSize)
                .Select(asset => new MediaLibraryQueryItem(
                    asset.Id,
                    asset.SourceId,
                    asset.Origin,
                    asset.Kind,
                    asset.SourceEntityId,
                    asset.ParentEntityId,
                    asset.ContextKey,
                    asset.CollectionKey,
                    asset.ContextTitle,
                    asset.ContextSubtitle,
                    asset.SourceLabel,
                    asset.Title,
                    asset.Caption,
                    asset.OriginalFileName,
                    asset.MediaDateUtc,
                    asset.Width,
                    asset.Height,
                    asset.DurationSeconds,
                    asset.IsCover,
                    asset.SortOrder,
                    asset.CacheVersion,
                    asset.VersionToken))
                .ToListAsync(cancellationToken);

            primaryStopwatch.Stop();
            _diagnostics.RecordSuccess(MediaLibraryQueryOperation.PrimaryTimeline, primaryStopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            primaryStopwatch.Stop();
            var diagnostic = _diagnostics.RecordFailure(
                MediaLibraryQueryOperation.PrimaryTimeline,
                ex,
                primaryStopwatch.ElapsedMilliseconds);
            _logger.LogWarning(ex,
                "Unified media timeline query failed. Reference {Reference}. The Photos page will use its PRISM-owned fallback.",
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
                var rows = await BuildBaseQuery()
                    .Where(asset => asset.ProjectId.HasValue)
                    .Select(asset => new { Id = asset.ProjectId!.Value, Name = asset.ContextTitle })
                    .Distinct()
                    .OrderBy(project => project.Name)
                    .ThenBy(project => project.Id)
                    .ToListAsync(cancellationToken);
                return rows.Select(row => new MediaLibraryProjectOption(row.Id, row.Name)).ToList();
            },
            Array.Empty<MediaLibraryProjectOption>(),
            warnings,
            cancellationToken);

        var hasPrismCatalogue = await ExecuteOptionalAsync(
            MediaLibraryQueryOperation.PrismSourceStatus,
            async () => await _db.Sources
                .AsNoTracking()
                .AnyAsync(source => source.Key == MediaSourceBootstrapper.PrismSourceKey && !source.IsDeleted, cancellationToken),
            true,
            warnings,
            cancellationToken);

        var summaryWarning = warnings.Count == 0
            ? null
            : $"The media timeline is available, but {warnings.Count} optional catalogue feature(s) are degraded. Reference {warnings[0].Reference}.";

        return new MediaLibraryQueryResult(
            items,
            projects,
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
        => _db.Assets
            .AsNoTracking()
            .Where(asset => asset.IsAvailable
                            && asset.AvailabilityStatus == MediaAvailabilityStatus.Available
                            && !asset.IsDeleted
                            && !asset.IsArchived)
            .Where(asset => !asset.Source.IsDeleted)
            .Where(asset => asset.Origin != MediaAssetOrigin.ExternalFile
                            || (_options.IsExternalSourceFeatureEnabled
                                && asset.Source.IsEnabled
                                && asset.Source.IsVisibleInLibrary
                                && asset.Source.SourceType == MediaLibrarySourceType.FileSystem));

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
        catch (Exception ex)
        {
            stopwatch.Stop();
            var diagnostic = _diagnostics.RecordFailure(operation, ex, stopwatch.ElapsedMilliseconds);
            warnings.Add(ToWarning(diagnostic));
            _logger.LogWarning(ex,
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

    private static IQueryable<MediaAsset> ApplySource(IQueryable<MediaAsset> query, string source)
        => source switch
        {
            "projects" => query.Where(asset => asset.Origin == MediaAssetOrigin.ProjectPhoto
                                                || asset.Origin == MediaAssetOrigin.ProjectVideo),
            "visits" => query.Where(asset => asset.Origin == MediaAssetOrigin.VisitPhoto),
            "events" => query.Where(asset => asset.Origin == MediaAssetOrigin.SocialMediaEventPhoto),
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

    private static IQueryable<MediaAsset> ApplyClassification(IQueryable<MediaAsset> query, string classification)
        => classification switch
        {
            "photograph" => query.Where(asset => asset.Classification == MediaClassification.Photograph),
            "screenshot" => query.Where(asset => asset.Classification == MediaClassification.Screenshot),
            "unknown" => query.Where(asset => asset.Classification == MediaClassification.Unknown),
            _ => query
        };

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
