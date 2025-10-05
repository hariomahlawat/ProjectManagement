using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Services.Notifications;
using ProjectManagement.ViewModels.Notifications;

namespace ProjectManagement.ViewComponents;

public sealed class NotificationBellViewComponent : ViewComponent
{
    private readonly UserNotificationService _notifications;
    private readonly LinkGenerator _linkGenerator;

    public NotificationBellViewComponent(UserNotificationService notifications, LinkGenerator linkGenerator)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _linkGenerator = linkGenerator ?? throw new ArgumentNullException(nameof(linkGenerator));
    }

    public async Task<IViewComponentResult> InvokeAsync(CancellationToken cancellationToken = default)
    {
        var model = HttpContext.User.Identity?.IsAuthenticated == true
            ? await BuildAuthenticatedModelAsync(HttpContext.User, cancellationToken)
            : BuildAnonymousModel();

        return View(model);
    }

    private NotificationBellViewModel BuildAnonymousModel()
    {
        return new NotificationBellViewModel
        {
            IsAuthenticated = false,
            Notifications = Array.Empty<NotificationDisplayModel>(),
            NotificationCenterUrl = _linkGenerator.GetPathByPage(HttpContext, page: "/Notifications/Index") ?? "/Notifications",
            ApiBaseUrl = Url.Content("~/api/notifications"),
            UnreadCountUrl = Url.Content("~/api/notifications/count"),
            HubUrl = Url.Content("~/hubs/notifications"),
            RecentLimit = 10,
        };
    }

    private async Task<NotificationBellViewModel> BuildAuthenticatedModelAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return BuildAnonymousModel();
        }

        const int limit = 10;
        var options = new NotificationListOptions
        {
            Limit = limit,
        };

        var recent = await _notifications.ListAsync(principal, userId, options, cancellationToken);
        var unreadCount = await _notifications.CountUnreadAsync(principal, userId, cancellationToken);

        var displayItems = recent
            .Select(NotificationDisplayModel.FromContract)
            .ToList();

        return new NotificationBellViewModel
        {
            IsAuthenticated = true,
            Notifications = displayItems,
            UnreadCount = unreadCount,
            NotificationCenterUrl = _linkGenerator.GetPathByPage(HttpContext, page: "/Notifications/Index") ?? "/Notifications",
            ApiBaseUrl = Url.Content("~/api/notifications"),
            UnreadCountUrl = Url.Content("~/api/notifications/count"),
            HubUrl = Url.Content("~/hubs/notifications"),
            RecentLimit = limit,
        };
    }
}
