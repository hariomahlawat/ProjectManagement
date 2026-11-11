using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

[Authorize]
public sealed class MediaModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaDerivativeService _derivatives;
    private readonly IMediaContentProviderResolver _contentResolver;
    private readonly MediaLibraryOptions _options;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IMemoryCache _failures;
    private readonly ILogger<MediaModel> _logger;

    // A derivative that cannot be produced (corrupt or oversized image, or content that is
    // gone) would otherwise be re-read and re-decoded on every request for its tile. The
    // failure is remembered per cache version, so replaced content is tried again at once.
    private static readonly TimeSpan PermanentFailureBackOff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan UnavailableBackOff = TimeSpan.FromMinutes(2);

    public MediaModel(MediaLibraryDbContext db, IMediaDerivativeService derivatives,
        IMediaContentProviderResolver contentResolver, IOptions<MediaLibraryOptions> options,
        IMediaAssetVisibilityPolicy visibility, IMemoryCache failures, ILogger<MediaModel> logger)
    {
        _db = db; _derivatives = derivatives; _contentResolver = contentResolver;
        _options = options.Value; _visibility = visibility; _failures = failures; _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(long id, string? variant, bool download = false,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsCatalogueEnabled) return NotFound();

        MediaAsset? asset;
        try
        {
            asset = await _db.Assets.AsNoTracking().Include(x => x.Source)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(ex, "Media catalogue is unavailable while serving asset {AssetId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (asset is null || !_visibility.IsVisible(asset))
            return NotFound();

        variant = variant?.Trim().ToLowerInvariant() switch
        {
            "thumb" => "thumb",
            "preview" => "preview",
            "original" => "original",
            _ => asset.Kind == MediaAssetKind.Photo ? "preview" : "original"
        };

        try
        {
            if (variant is "thumb" or "preview")
            {
                if (asset.Kind != MediaAssetKind.Photo) return NotFound();
                if (_failures.TryGetValue(FailureKey(asset, variant), out _)) return NotFound();
                var path = await _derivatives.EnsureAsync(asset.Id, variant, cancellationToken);
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                Response.Headers.CacheControl = "private,max-age=86400";
                return new FileStreamResult(stream, "image/webp") { LastModified = asset.FileModifiedAtUtc };
            }

            var content = await _contentResolver.ResolveAsync(asset, cancellationToken);
            if (content is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            var original = await content.OpenReadAsync(cancellationToken);
            Response.Headers.CacheControl = "private,max-age=300";
            return new FileStreamResult(original, content.ContentType)
            {
                EnableRangeProcessing = asset.Kind == MediaAssetKind.Video,
                FileDownloadName = download ? asset.OriginalFileName : null,
                LastModified = content.LastModifiedUtc ?? asset.FileModifiedAtUtc
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (MediaContentUnavailableException ex)
        {
            // The content is gone or no provider can open it. A 404 lets the grid show its
            // placeholder; an unhandled exception made every such tile a logged 500.
            _logger.LogWarning(ex, "Media asset {AssetId} has no readable content", id);
            _failures.Set(FailureKey(asset, variant), true, UnavailableBackOff);
            return NotFound();
        }
        catch (MediaProcessingPermanentException ex)
        {
            _logger.LogInformation(ex, "Media asset {AssetId} cannot be rendered as {Variant}: {Reason}", id, variant, ex.Message);
            _failures.Set(FailureKey(asset, variant), true, PermanentFailureBackOff);
            return NotFound();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Unable to serve media asset {AssetId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string FailureKey(MediaAsset asset, string variant)
        => $"photos-media-failure:{asset.Id}:{asset.CacheVersion}:{variant}";
}
