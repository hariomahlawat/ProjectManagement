using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.ViewModels.Navigation;

namespace ProjectManagement.ViewComponents;

public sealed class AotsUnreadBadgeViewComponent : ViewComponent
{
    // SECTION: Dependencies
    private readonly IAotsUnreadService _aotsUnreadService;

    public AotsUnreadBadgeViewComponent(IAotsUnreadService aotsUnreadService)
    {
        _aotsUnreadService = aotsUnreadService ?? throw new ArgumentNullException(nameof(aotsUnreadService));
    }

    // SECTION: View component entry point
    public async Task<IViewComponentResult> InvokeAsync(string variant = "default", CancellationToken cancellationToken = default)
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var count = await _aotsUnreadService.GetUnreadCountAsync(userId, cancellationToken);

        if (count <= 0)
        {
            return View(new AotsUnreadBadgeViewModel
            {
                ShowBadge = false,
                Variant = variant
            });
        }

        return View(new AotsUnreadBadgeViewModel
        {
            ShowBadge = true,
            DisplayText = AotsUnreadBadgeFormatter.Format(count),
            AriaLabel = $"Unread AOTS documents: {count}",
            Variant = variant
        });
    }
}
