using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Services.Notifications;
using ProjectManagement.ViewModels.Notifications;

namespace ProjectManagement.Pages.Notifications;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly UserNotificationService _notifications;
    private readonly LinkGenerator _linkGenerator;

    public NotificationIndexViewModel ViewModel { get; private set; } = new();

    public IndexModel(UserNotificationService notifications, LinkGenerator linkGenerator)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _linkGenerator = linkGenerator ?? throw new ArgumentNullException(nameof(linkGenerator));
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        const int pageSize = 30;
        var page = await _notifications.ListPageAsync(
            User,
            userId,
            new NotificationListOptions
            {
                Limit = pageSize,
                IncludeMuted = true,
            },
            cancellationToken);

        ViewModel = new NotificationIndexViewModel
        {
            Notifications = page.Items.Select(NotificationDisplayModel.FromContract).ToList(),
            Projects = page.Projects
                .Select(project => new ProjectFilterOption(project.Id, project.Label, project.IsMuted))
                .ToList(),
            Modules = page.Modules,
            UnreadCount = page.UnreadCount,
            TotalCount = page.TotalCount,
            NextCursor = page.NextCursor,
            HasMore = page.HasMore,
            ApiBaseUrl = Url.Content("~/api/notifications"),
            UnreadCountUrl = Url.Content("~/api/notifications/count"),
            HubUrl = Url.Content("~/hubs/notifications"),
            NotificationCenterUrl = _linkGenerator.GetPathByPage(HttpContext, page: "/Notifications/Index") ?? "/Notifications",
            PageSize = pageSize,
        };

        return Page();
    }
}
