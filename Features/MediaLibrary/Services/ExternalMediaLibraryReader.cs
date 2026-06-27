using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Optional read adapter for catalogued external folders. Every infrastructure failure
/// is contained here so the main Photos page can continue serving PRISM-owned media.
/// </summary>
public sealed class ExternalMediaLibraryReader : IExternalMediaLibraryReader
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<ExternalMediaLibraryReader> _logger;

    public ExternalMediaLibraryReader(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        ILogger<ExternalMediaLibraryReader> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExternalMediaSearchResult> SearchAsync(
        ExternalMediaSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsExternalSourceFeatureEnabled)
        {
            return ExternalMediaSearchResult.Empty();
        }

        try
        {
            var query = _db.Assets
                .AsNoTracking()
                .Where(asset => asset.Origin == MediaAssetOrigin.ExternalFile
                                && asset.IsAvailable
                                && !asset.IsDeleted
                                && !asset.IsArchived
                                && asset.Source.SourceType == MediaLibrarySourceType.FileSystem
                                && asset.Source.IsVisibleInLibrary
                                && !asset.Source.IsDeleted);

            query = request.Kind switch
            {
                "photo" => query.Where(asset => asset.Kind == MediaAssetKind.Photo),
                "video" => query.Where(asset => asset.Kind == MediaAssetKind.Video),
                _ => query
            };

            query = request.Classification switch
            {
                "screenshot" => query.Where(asset => asset.Classification == MediaClassification.Screenshot),
                "photograph" => query.Where(asset => asset.Classification == MediaClassification.Photograph),
                "unknown" => query.Where(asset => asset.Classification == MediaClassification.Unknown),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                var pattern = $"%{EscapeLikePattern(request.Query.Trim())}%";
                query = query.Where(asset =>
                    EF.Functions.ILike(asset.Title, pattern)
                    || (asset.Caption != null && EF.Functions.ILike(asset.Caption, pattern))
                    || EF.Functions.ILike(asset.ContextTitle, pattern)
                    || EF.Functions.ILike(asset.ContextSubtitle, pattern)
                    || EF.Functions.ILike(asset.OriginalFileName, pattern)
                    || (asset.RelativePath != null && EF.Functions.ILike(asset.RelativePath, pattern))
                    || EF.Functions.ILike(asset.Source.Name, pattern));
            }

            var queryWithoutYear = query;
            if (request.Year.HasValue)
            {
                query = query.Where(asset => asset.MediaDateUtc.Year == request.Year.Value);
            }

            var total = await query.CountAsync(cancellationToken);
            var photos = await query.CountAsync(asset => asset.Kind == MediaAssetKind.Photo, cancellationToken);
            var videos = await query.CountAsync(asset => asset.Kind == MediaAssetKind.Video, cancellationToken);
            var collections = await query.Select(asset => asset.CollectionKey).Distinct().CountAsync(cancellationToken);
            var years = await queryWithoutYear
                .Select(asset => asset.MediaDateUtc.Year)
                .Distinct()
                .OrderByDescending(year => year)
                .ToListAsync(cancellationToken);

            // Read only the external slice needed to compose the requested global page.
            // This avoids the prototype's silent 1,000-item browse ceiling and keeps deep
            // pages bounded even when an external archive contains many thousands of files.
            var skip = Math.Max(0, request.Skip);
            var take = Math.Max(1, request.Take);
            var items = await query
                .OrderByDescending(asset => asset.MediaDateUtc)
                .ThenBy(asset => asset.ContextTitle)
                .ThenBy(asset => asset.SortOrder)
                .ThenBy(asset => asset.Id)
                .Skip(skip)
                .Take(take)
                .Select(asset => new ExternalMediaSearchItem(
                    asset.Id,
                    asset.Kind,
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
                    asset.SortOrder,
                    asset.CacheVersion,
                    asset.ParentEntityId))
                .ToListAsync(cancellationToken);

            return new ExternalMediaSearchResult(
                items,
                total,
                photos,
                videos,
                collections,
                years,
                true,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsOptionalInfrastructureFailure(ex))
        {
            _logger.LogWarning(ex,
                "External media catalogue is unavailable. Core PRISM Photos will continue without external items.");
            return ExternalMediaSearchResult.Empty(
                available: false,
                warning: "External folders are temporarily unavailable. PRISM photos remain available.");
        }
    }

    private static bool IsOptionalInfrastructureFailure(Exception exception)
        => exception is NpgsqlException
            or DbUpdateException
            or InvalidOperationException
            or TimeoutException;

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
