using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.Albums;

[Authorize]
[ValidateAntiForgeryToken]
public sealed class ActionsModel : PageModel
{
    private readonly IMediaAlbumService _albums;
    private readonly ILogger<ActionsModel> _logger;

    public ActionsModel(IMediaAlbumService albums, ILogger<ActionsModel> logger)
    {
        _albums = albums ?? throw new ArgumentNullException(nameof(albums));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IActionResult> OnPostCreateAsync(
        string name,
        string? description,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _albums.CreateAsync(
            name,
            description,
            Array.Empty<long>(),
            CurrentActor(),
            cancellationToken);
        StoreResult(result);
        return result.Succeeded && result.AlbumId.HasValue
            ? RedirectToAlbum(result.AlbumId.Value)
            : RedirectLocal(returnUrl, albumsWorkspace: true);
    }

    public async Task<IActionResult> OnPostAddItemsAsync(
        Guid? albumId,
        string? newAlbumName,
        string? newAlbumDescription,
        long[]? assetIds,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var selected = NormalizeIds(assetIds);
        MediaAlbumMutationResult result;
        if (albumId.HasValue && albumId.Value != Guid.Empty)
        {
            result = await _albums.AddItemsAsync(
                albumId.Value,
                selected,
                CurrentActor(),
                cancellationToken);
        }
        else
        {
            result = await _albums.CreateAsync(
                newAlbumName ?? string.Empty,
                newAlbumDescription,
                selected,
                CurrentActor(),
                cancellationToken);
        }

        StoreResult(result);
        return RedirectLocal(returnUrl, albumsWorkspace: false);
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        Guid albumId,
        string name,
        string? description,
        Guid concurrencyToken,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _albums.UpdateMetadataAsync(
            albumId,
            name,
            description,
            concurrencyToken,
            CurrentActor(),
            cancellationToken);
        StoreResult(result);
        return RedirectLocal(returnUrl, albumId: albumId);
    }

    public async Task<IActionResult> OnPostArchiveAsync(
        Guid albumId,
        Guid concurrencyToken,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _albums.SetArchivedAsync(
            albumId,
            archived: true,
            concurrencyToken,
            CurrentActor(),
            cancellationToken);
        StoreResult(result);
        return result.Succeeded
            ? RedirectToPage("/Photos/Index", new { View = "collections", CollectionTab = "albums" })
            : RedirectLocal(returnUrl, albumId: albumId);
    }

    public async Task<IActionResult> OnPostRestoreAsync(
        Guid albumId,
        Guid concurrencyToken,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _albums.SetArchivedAsync(
            albumId,
            archived: false,
            concurrencyToken,
            CurrentActor(),
            cancellationToken);
        StoreResult(result);
        return RedirectLocal(returnUrl, albumId: albumId);
    }

    public async Task<IActionResult> OnPostRemoveItemsAsync(
        Guid albumId,
        long[]? assetIds,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _albums.RemoveItemsAsync(
            albumId,
            NormalizeIds(assetIds),
            CurrentActor(),
            cancellationToken);
        StoreResult(result);
        return RedirectLocal(returnUrl, albumId: albumId);
    }

    public async Task<IActionResult> OnPostSetCoverAsync(
        Guid albumId,
        long assetId,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _albums.SetCoverAsync(
            albumId,
            assetId,
            CurrentActor(),
            cancellationToken);
        StoreResult(result);
        return RedirectLocal(returnUrl, albumId: albumId);
    }

    public async Task<IActionResult> OnPostReorderAsync(
        Guid albumId,
        long[]? orderedAssetIds,
        CancellationToken cancellationToken)
    {
        var result = await _albums.ReorderAsync(
            albumId,
            NormalizeIdsPreserveOrder(orderedAssetIds),
            CurrentActor(),
            cancellationToken);

        if (!result.Succeeded)
        {
            Response.StatusCode = result.Failure is MediaAlbumMutationFailure.Forbidden ? 403 : 400;
        }

        return new JsonResult(new
        {
            success = result.Succeeded,
            message = result.Message,
            affected = result.AffectedCount
        });
    }

    public async Task<IActionResult> OnPostUpdateCaptionAsync(
        long assetId,
        string? caption,
        Guid? concurrencyToken,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _albums.UpdateEditorialCaptionAsync(
            assetId,
            caption,
            concurrencyToken,
            CurrentActor(),
            cancellationToken);
        StoreResult(result);
        return RedirectLocal(returnUrl, albumsWorkspace: false);
    }

    private MediaAlbumActor CurrentActor()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Authenticated Photos user identifier is unavailable.");
        }

        return new MediaAlbumActor(
            userId,
            User.IsInRole("Admin") || User.IsInRole("HoD") || User.IsInRole("Comdt"));
    }

    private void StoreResult(MediaAlbumMutationResult result)
    {
        TempData[result.Succeeded ? "PhotosSuccess" : "PhotosError"] = result.Message;
        if (!result.Succeeded)
        {
            _logger.LogInformation(
                "Media album action rejected. Failure={Failure}; AlbumId={AlbumId}; Message={Message}",
                result.Failure,
                result.AlbumId,
                result.Message);
        }
    }

    private IActionResult RedirectLocal(
        string? returnUrl,
        bool albumsWorkspace = false,
        Guid? albumId = null)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        if (albumId.HasValue)
        {
            return RedirectToAlbum(albumId.Value);
        }

        return RedirectToPage("/Photos/Index", albumsWorkspace
            ? new { View = "collections", CollectionTab = "albums" }
            : new { View = "photos" });
    }

    private IActionResult RedirectToAlbum(Guid albumId)
        => RedirectToPage("/Photos/Index", new { View = "album", AlbumId = albumId });

    private static long[] NormalizeIds(long[]? ids)
        => (ids ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .Take(MediaAlbumService.MaximumAlbumItems)
            .ToArray();

    private static long[] NormalizeIdsPreserveOrder(long[]? ids)
        => NormalizeIds(ids);
}
