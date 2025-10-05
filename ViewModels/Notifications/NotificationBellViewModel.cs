using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Notifications;

public sealed record NotificationBellViewModel
{
    public bool IsAuthenticated { get; init; }

    public int UnreadCount { get; init; }

    public IReadOnlyList<NotificationDisplayModel> Notifications { get; init; } = new List<NotificationDisplayModel>();

    public string NotificationCenterUrl { get; init; } = string.Empty;

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string UnreadCountUrl { get; init; } = string.Empty;

    public string HubUrl { get; init; } = string.Empty;

    public int RecentLimit { get; init; } = 10;
}
