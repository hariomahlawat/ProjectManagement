using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.ViewComponents;

/// <summary>
/// On the Repository trash: a line pointing to "Restored but still hidden" while that list has
/// documents on it, and nothing once it is empty. Only people allowed to use the list see it.
/// </summary>
public sealed class RestoredHiddenNoticeViewComponent : ViewComponent
{
    private readonly DocRepoRestoredHiddenService _service;

    public RestoredHiddenNoticeViewComponent(DocRepoRestoredHiddenService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!await _service.CanManageAsync(HttpContext.User))
        {
            return Content(string.Empty);
        }

        var count = await _service.CountAsync(HttpContext.RequestAborted);
        return count == 0 ? Content(string.Empty) : View(count);
    }
}
