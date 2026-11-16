using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Features.Collage;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.CollageStickers;

/// <summary>
/// Administration › Crests and logos (docs/ux/collage-studio-library-and-unit-stickers.md, PR 3):
/// the stickers everyone can put on a collage. Uploads, names, order, retiring and restoring all go
/// through <see cref="ICollageStickerService"/>, which checks the file, re-encodes it and writes the
/// audit entry. Nothing here deletes a picture.
/// </summary>
[Authorize(Policy = AdminPolicies.CollageStickersManage)]
// The service refuses anything over 5 MB; the request limit leaves room for the form around it.
[RequestFormLimits(MultipartBodyLengthLimit = IndexModel.RequestLimitBytes)]
[RequestSizeLimit(IndexModel.RequestLimitBytes)]
[IndexModel.RefuseOversizeUpload]
public sealed class IndexModel : PageModel
{
    public const long RequestLimitBytes = 6_000_000;

    private readonly ICollageStickerService _stickers;

    public IndexModel(ICollageStickerService stickers)
    {
        _stickers = stickers ?? throw new ArgumentNullException(nameof(stickers));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<CollageSticker> Active { get; private set; } = Array.Empty<CollageSticker>();

    public IReadOnlyList<CollageSticker> Retired { get; private set; } = Array.Empty<CollageSticker>();

    public int MaxActive => CollageStickerService.MaxActive;

    public int NameMaxLength => CollageSticker.NameMaxLength;

    public int SearchWordsMaxLength => CollageSticker.SearchWordsMaxLength;

    public bool IsFull => Active.Count >= MaxActive;

    public static string PictureUrl(CollageSticker sticker) => CollageApi.PictureUrl(sticker.Id, sticker.Version);

    /// <summary>A sticker's picture under the app's path base, for the thumbnails on this page.</summary>
    public static string PictureUrl(CollageSticker sticker, PathString pathBase) => CollageApi.PictureUrl(sticker.Id, sticker.Version, pathBase);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var all = await _stickers.ListAllAsync(cancellationToken);
        Active = all.Where(s => !s.IsRetired).ToList();
        Retired = all.Where(s => s.IsRetired).ToList();
        Header = new AdminPageHeaderModel
        {
            Title = "Crests and logos",
            Description = "The crests and logos everyone can put on a collage in Collage Studio, under Stickers › Crests and logos. They appear in the order shown here. A retired one leaves the picker, but collages that already use it keep it.",
            Icon = "bi-patch-check"
        };
    }

    public async Task<IActionResult> OnPostAddAsync(IFormFile? picture, string? name, string? searchWords, CancellationToken cancellationToken)
    {
        if (picture is null || picture.Length == 0)
        {
            return Report(CollageStickerOutcome.Refused("Choose a picture to upload."));
        }

        await using var stream = picture.OpenReadStream();
        return Report(await _stickers.AddAsync(stream, picture.FileName, name ?? string.Empty, searchWords, UserId(), cancellationToken));
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, string? name, string? searchWords, CancellationToken cancellationToken) =>
        Report(await _stickers.RenameAsync(id, name ?? string.Empty, searchWords, UserId(), cancellationToken));

    public async Task<IActionResult> OnPostMoveAsync(int id, bool up, CancellationToken cancellationToken) =>
        Report(await _stickers.MoveAsync(id, up, UserId(), cancellationToken));

    public async Task<IActionResult> OnPostRetireAsync(int id, CancellationToken cancellationToken) =>
        Report(await _stickers.RetireAsync(id, UserId(), cancellationToken));

    public async Task<IActionResult> OnPostRestoreAsync(int id, CancellationToken cancellationToken) =>
        Report(await _stickers.RestoreAsync(id, UserId(), cancellationToken));

    private IActionResult Report(CollageStickerOutcome outcome)
    {
        TempData[outcome.Succeeded ? FlashMessageKeys.AdminCollageStickersSuccess : FlashMessageKeys.AdminCollageStickersError] = outcome.Message;
        return RedirectToPage();
    }

    private string? UserId() => UserIdOf(User);

    private static string? UserIdOf(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>
    /// An upload over the request limit would meet the framework's bare "413 Payload Too Large"
    /// page once the form is read. This filter looks at the declared length first and answers as
    /// the page does for any refused picture: a plain message on the page and an audit entry. It
    /// runs after the page's authorization (order 0), so only administrators reach it, and before
    /// the size limit (900) and the antiforgery check (1000), which would read the body. The body
    /// is never read here; a request with no declared length still meets the framework's limit.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class RefuseOversizeUploadAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
    {
        public int Order => 500;

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var request = context.HttpContext.Request;
            if (context.Result is not null
                || !HttpMethods.IsPost(request.Method)
                || request.ContentLength is not long length
                || length <= RequestLimitBytes)
            {
                return;
            }

            var services = context.HttpContext.RequestServices;
            var outcome = await services.GetRequiredService<ICollageStickerService>()
                .RefuseOversizeRequestAsync(length, UserIdOf(context.HttpContext.User), context.HttpContext.RequestAborted);
            var tempData = services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context.HttpContext);
            tempData[FlashMessageKeys.AdminCollageStickersError] = outcome.Message;
            // Stopping here skips the filter that normally saves TempData, so it is saved now.
            tempData.Save();
            context.Result = new RedirectToPageResult("/CollageStickers/Index", new { area = "Admin" });
        }
    }
}
