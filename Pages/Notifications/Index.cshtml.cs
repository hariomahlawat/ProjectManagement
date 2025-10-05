using System;
using System.Collections.Generic;
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

        const int pageSize = 50;
        var options = new NotificationListOptions
        {
            Limit = pageSize,
        };

        var notifications = await _notifications.ListAsync(User, userId, options, cancellationToken);
        var unreadCount = await _notifications.CountUnreadAsync(User, userId, cancellationToken);

        var displayItems = notifications
            .Select(NotificationDisplayModel.FromContract)
            .ToList();

        var projectOptions = displayItems
            .Where(n => n.ProjectId.HasValue)
            .GroupBy(n => n.ProjectId!.Value)
            .Select(group =>
            {
                var label = group
                    .Select(item => item.ProjectName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? $"Project #{group.Key}";
                return new ProjectFilterOption(group.Key, label);
            })
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ViewModel = new NotificationIndexViewModel
        {
            Notifications = displayItems,
            Projects = projectOptions,
            UnreadCount = unreadCount,
            ApiBaseUrl = Url.Content("~/api/notifications"),
            UnreadCountUrl = Url.Content("~/api/notifications/count"),
            HubUrl = Url.Content("~/hubs/notifications"),
            NotificationCenterUrl = _linkGenerator.GetPathByPage(HttpContext, page: "/Notifications/Index") ?? "/Notifications",
            PageSize = pageSize,
        };

        return Page();
    }
}
