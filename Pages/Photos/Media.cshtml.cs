using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

[Authorize]
public sealed class MediaModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaDerivativeService _derivatives;
    private readonly INetworkSharePathResolver _pathResolver;
    private readonly ILogger<MediaModel> _logger;

    public MediaModel(
        MediaLibraryDbContext db,
        IMediaDerivativeService derivatives,
        INetworkSharePathResolver pathResolver,
        ILogger<MediaModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _derivatives = derivatives ?? throw new ArgumentNullException(nameof(derivatives));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IActionResult> OnGetAsync(
        long id,
        string? variant,
        bool download = false,
        CancellationToken cancellationToken = default)
    {
        var asset = await _db.Assets
            .AsNoTracking()
            .Include(item => item.Source)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (asset is null || !asset.IsAvailable || asset.IsDeleted || !asset.Source.IsEnabled)
        {
            return NotFound();
        }

        if (asset.Source.SourceType != MediaLibrarySourceType.NetworkShare
            || string.IsNullOrWhiteSpace(asset.Source.RootPath)
            || string.IsNullOrWhiteSpace(asset.RelativePath))
        {
            return NotFound();
        }

        variant = variant?.Trim().ToLowerInvariant() switch
        {
            "thumb" => "thumb",
            "preview" => "preview",
            "original" => "original",
            _ => asset.Kind == MediaAssetKind.Photo ? "preview" : "original"
        };

        try
        {
            string path;
            string contentType;
            var enableRangeProcessing = false;

            if (variant is "thumb" or "preview")
            {
                if (asset.Kind != MediaAssetKind.Photo)
                {
                    return NotFound();
                }

                path = await _derivatives.EnsureAsync(asset.Id, variant, cancellationToken);
                contentType = "image/webp";
                Response.Headers.CacheControl = "private,max-age=86400";
            }
            else
            {
                path = _pathResolver.ResolveAssetPath(asset.Source.RootPath, asset.RelativePath);
                if (!System.IO.File.Exists(path))
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                contentType = asset.ContentType;
                enableRangeProcessing = asset.Kind == MediaAssetKind.Video;
                Response.Headers.CacheControl = "private,max-age=300";
            }

            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return new FileStreamResult(stream, contentType)
            {
                EnableRangeProcessing = enableRangeProcessing,
                FileDownloadName = download ? asset.OriginalFileName : null,
                LastModified = asset.FileModifiedAtUtc
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Unable to serve media asset {AssetId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
