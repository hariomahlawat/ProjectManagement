using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Notifications;

public sealed record NotificationIndexViewModel
{
    public IReadOnlyList<NotificationDisplayModel> Notifications { get; init; } = new List<NotificationDisplayModel>();

    public IReadOnlyList<ProjectFilterOption> Projects { get; init; } = new List<ProjectFilterOption>();

    public int UnreadCount { get; init; }

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string UnreadCountUrl { get; init; } = string.Empty;

    public string HubUrl { get; init; } = string.Empty;

    public string NotificationCenterUrl { get; init; } = string.Empty;

    public int PageSize { get; init; } = 50;
}

public sealed record ProjectFilterOption(int Id, string Label);
