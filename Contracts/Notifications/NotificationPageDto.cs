using System;
using System.Collections.Generic;

namespace ProjectManagement.Contracts.Notifications;

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationListItem> Items,
    int TotalCount,
    int UnreadCount,
    string? NextCursor,
    bool HasMore,
    IReadOnlyList<NotificationProjectOptionDto> Projects,
    IReadOnlyList<string> Modules);

public sealed record NotificationProjectOptionDto(int Id, string Label, bool IsMuted);

public sealed record NotificationIdsRequest(IReadOnlyCollection<int>? Ids);

public sealed record NotificationMutationDto(
    IReadOnlyList<int> NotificationIds,
    bool IsRead,
    DateTime? ReadUtc,
    DateTime? SeenUtc,
    bool AppliesToAll,
    int AffectedCount,
    int UnreadCount);

public sealed record NotificationSeenDto(
    IReadOnlyList<int> NotificationIds,
    DateTime SeenUtc,
    int AffectedCount);

public sealed record NotificationProjectMuteDto(
    int ProjectId,
    bool IsMuted,
    IReadOnlyList<int> ChangedNotificationIds,
    int UnreadCount);
