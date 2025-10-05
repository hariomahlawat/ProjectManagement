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
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Notifications;
using ProjectManagement.ViewModels.Notifications;

namespace ProjectManagement.Pages.Notifications;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly UserNotificationService _notifications;
    private readonly ApplicationDbContext _db;
    private readonly LinkGenerator _linkGenerator;

    public NotificationIndexViewModel ViewModel { get; private set; } = new();

    public IndexModel(UserNotificationService notifications, ApplicationDbContext db, LinkGenerator linkGenerator)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _db = db ?? throw new ArgumentNullException(nameof(db));
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

        var projectIds = displayItems
            .Where(n => n.ProjectId.HasValue)
            .Select(n => n.ProjectId!.Value)
            .Distinct()
            .ToList();

        Dictionary<int, string>? projectNames = null;
        if (projectIds.Count > 0)
        {
            projectNames = await _db.Projects
                .AsNoTracking()
                .Where(p => projectIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        }

        if (projectNames is { Count: > 0 })
        {
            displayItems = displayItems
                .Select(item => item.ProjectId is int pid && projectNames.TryGetValue(pid, out var name)
                    ? item with { ProjectName = name }
                    : item)
                .ToList();
        }

        var projectOptions = projectIds
            .Select(id =>
            {
                var label = projectNames != null && projectNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : $"Project #{id}";
                return new ProjectFilterOption(id, label);
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
