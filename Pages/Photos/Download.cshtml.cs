using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

[Authorize]
public sealed class DownloadModel : PageModel
{
    private readonly IMediaBulkDownloadService _downloads;
    private readonly ILogger<DownloadModel> _logger;

    public DownloadModel(
        IMediaBulkDownloadService downloads,
        ILogger<DownloadModel> logger)
    {
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IActionResult> OnPostAsync(
        long[]? assetIds,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.Identity?.Name
                     ?? "unknown";

        MediaBulkDownloadResult result;
        try
        {
            result = await _downloads.CreateAsync(
                assetIds ?? Array.Empty<long>(),
                userId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected Photos bulk-download failure for user {UserId}.",
                userId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "The ZIP could not be created. No download was started; try again or contact an administrator if the problem persists.");
        }

        if (!result.Succeeded || result.Archive is null)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "The selected media could not be downloaded."
                : result.Message;

            return result.FailureReason switch
            {
                MediaBulkDownloadFailureReason.EmptySelection
                    or MediaBulkDownloadFailureReason.TooManyItems
                    or MediaBulkDownloadFailureReason.SourceBytesExceeded
                    => BadRequest(message),
                MediaBulkDownloadFailureReason.NoEligibleAssets
                    or MediaBulkDownloadFailureReason.NoReadableAssets
                    => NotFound(message),
                MediaBulkDownloadFailureReason.SourceReadFailed
                    => StatusCode(StatusCodes.Status409Conflict, message),
                _ => StatusCode(StatusCodes.Status500InternalServerError, message)
            };
        }

        Response.Headers["Cache-Control"] = "no-store, no-cache";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // FileStreamResult owns and disposes the stream after ASP.NET Core completes its
        // asynchronous response copy. The stream was opened with DeleteOnClose, so the
        // private temporary archive is removed automatically after the response finishes.
        return new FileStreamResult(result.Archive.Stream, "application/zip")
        {
            FileDownloadName = result.Archive.FileName,
            EnableRangeProcessing = false
        };
    }
}
