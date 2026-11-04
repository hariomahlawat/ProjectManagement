using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Notifications;

public sealed record NotificationBellViewModel
{
    public bool IsAuthenticated { get; init; }

    public int UnreadCount { get; init; }

    /// <summary>Drives the bell badge; clears once the recipient opens the bell.</summary>
    public int UnseenCount { get; init; }

    /// <summary>Newest notification id in the bell's snapshot; the seen-all boundary.</summary>
    public int? LatestNotificationId { get; init; }

    /// <summary>Snapshot version; a background refresh skips the list while it is unchanged.</summary>
    public string? ListVersion { get; init; }

    public IReadOnlyList<NotificationDisplayModel> Notifications { get; init; } = new List<NotificationDisplayModel>();

    public string NotificationCenterUrl { get; init; } = string.Empty;

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string UnreadCountUrl { get; init; } = string.Empty;

    public string HubUrl { get; init; } = string.Empty;

    public int RecentLimit { get; init; } = 10;

    /// <summary>Rows the bell loads to build groups from; it still shows RecentLimit entries.</summary>
    public const int GroupingWindow = 50;

    public int FetchLimit => GroupingWindow;
}
