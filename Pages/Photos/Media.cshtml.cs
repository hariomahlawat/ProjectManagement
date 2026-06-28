using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<MediaModel> _logger;

    public MediaModel(MediaLibraryDbContext db, IMediaDerivativeService derivatives,
        IMediaContentProviderResolver contentResolver, IOptions<MediaLibraryOptions> options,
        ILogger<MediaModel> logger)
    {
        _db = db; _derivatives = derivatives; _contentResolver = contentResolver;
        _options = options.Value; _logger = logger;
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

        if (asset is null || !asset.IsAvailable || asset.IsDeleted || asset.Source.IsDeleted)
            return NotFound();
        if (asset.Source.SourceType == MediaLibrarySourceType.FileSystem && !asset.Source.IsVisibleInLibrary)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Unable to serve media asset {AssetId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
