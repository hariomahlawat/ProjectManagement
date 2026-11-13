using System.Security.Claims;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using ProjectManagement.Services.Celebrations;

namespace ProjectManagement.ViewComponents;

/// <summary>
/// The birthday or anniversary wish for the signed-in person (docs/ux/celebration-wish.md).
/// Renders nothing on every other day; the script decides when the wish plays.
/// </summary>
public sealed class CelebrationWishViewComponent : ViewComponent
{
    private readonly ICelebrationWishService _wishes;

    public CelebrationWishViewComponent(ICelebrationWishService wishes)
    {
        _wishes = wishes ?? throw new ArgumentNullException(nameof(wishes));
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var wish = await _wishes.GetTodayAsync(userId, HttpContext.RequestAborted);
        return wish is null ? new HtmlContentViewComponentResult(HtmlString.Empty) : View(wish);
    }
}
