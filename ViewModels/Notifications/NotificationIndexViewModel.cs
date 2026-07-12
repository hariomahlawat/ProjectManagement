using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Notifications;

public sealed record NotificationIndexViewModel
{
    public IReadOnlyList<NotificationDisplayModel> Notifications { get; init; } = new List<NotificationDisplayModel>();
    public IReadOnlyList<ProjectFilterOption> Projects { get; init; } = new List<ProjectFilterOption>();
    public IReadOnlyList<string> Modules { get; init; } = new List<string>();
    public IReadOnlyList<NotificationFolderOption> Folders { get; init; } = new List<NotificationFolderOption>();
    public int UnreadCount { get; init; }
    public int TotalCount { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string UnreadCountUrl { get; init; } = string.Empty;
    public string HubUrl { get; init; } = string.Empty;
    public string NotificationCenterUrl { get; init; } = string.Empty;
    public int PageSize { get; init; } = 30;
}

public sealed record ProjectFilterOption(int Id, string Label, bool IsMuted = false);

public sealed record NotificationFolderOption(
    string Key,
    int TotalCount,
    int UnreadCount);
