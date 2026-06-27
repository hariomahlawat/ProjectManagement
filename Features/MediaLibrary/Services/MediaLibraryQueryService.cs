using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Read-optimised, single-query facade for the Photos experience. All media sources are
/// filtered, ordered and paged by PostgreSQL so cross-source chronology is stable.
/// </summary>
public sealed class MediaLibraryQueryService : IMediaLibraryQueryService
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<MediaLibraryQueryService> _logger;

    public MediaLibraryQueryService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        ILogger<MediaLibraryQueryService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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

        try
        {
            var baseQuery = _db.Assets
                .AsNoTracking()
                .Where(asset => asset.IsAvailable && !asset.IsDeleted && !asset.IsArchived)
                .Where(asset => !asset.Source.IsDeleted)
                .Where(asset => asset.Origin != MediaAssetOrigin.ExternalFile
                                || (_options.IsExternalSourceFeatureEnabled
                                    && asset.Source.IsEnabled
                                    && asset.Source.IsVisibleInLibrary
                                    && asset.Source.SourceType == MediaLibrarySourceType.FileSystem));

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

            var queryWithoutYear = baseQuery;
            if (request.Year.HasValue)
            {
                baseQuery = baseQuery.Where(asset => asset.MediaDateUtc.Year == request.Year.Value);
            }

            var total = await baseQuery.CountAsync(cancellationToken);
            var pageSize = Math.Clamp(request.PageSize, 1, 250);
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            var pageNumber = Math.Clamp(request.PageNumber, 1, pageCount);
            var skip = (pageNumber - 1) * pageSize;

            // A DbContext does not support concurrent operations. Keep these queries
            // sequential and cancellation-aware; the page query is still fully bounded.
            var items = await baseQuery
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

            var photos = await baseQuery.CountAsync(asset => asset.Kind == MediaAssetKind.Photo, cancellationToken);
            var videos = await baseQuery.CountAsync(asset => asset.Kind == MediaAssetKind.Video, cancellationToken);
            var collections = await baseQuery.Select(asset => asset.CollectionKey).Distinct().CountAsync(cancellationToken);
            var years = await queryWithoutYear
                .Select(asset => asset.MediaDateUtc.Year)
                .Distinct()
                .OrderByDescending(year => year)
                .ToListAsync(cancellationToken);
            var projects = await _db.Assets
                .AsNoTracking()
                .Where(asset => asset.IsAvailable && !asset.IsDeleted && !asset.IsArchived && asset.ProjectId != null)
                .GroupBy(asset => new { Id = asset.ProjectId!.Value, asset.ContextTitle })
                .Select(group => new MediaLibraryProjectOption(group.Key.Id, group.Key.ContextTitle))
                .OrderBy(project => project.Name)
                .ToListAsync(cancellationToken);
            var hasPrismCatalogue = await _db.Sources
                .AsNoTracking()
                .AnyAsync(source => source.Key == MediaSourceBootstrapper.PrismSourceKey && !source.IsDeleted, cancellationToken);

            return new MediaLibraryQueryResult(
                items,
                projects,
                years,
                new MediaLibraryStatistics(total, photos, videos, collections),
                pageNumber,
                pageSize,
                pageNumber > 1,
                total > skip + pageSize,
                true,
                hasPrismCatalogue,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsCatalogueFailure(ex))
        {
            _logger.LogWarning(ex,
                "Unified media catalogue query failed. The Photos page will use its PRISM-owned fallback.");
            return MediaLibraryQueryResult.Unavailable(
                request.PageNumber,
                request.PageSize,
                "The media catalogue is temporarily unavailable. PRISM-owned media remains available.");
        }
    }

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

    private static bool IsCatalogueFailure(Exception exception)
        => exception is NpgsqlException
            or DbUpdateException
            or InvalidOperationException
            or TimeoutException;
}
