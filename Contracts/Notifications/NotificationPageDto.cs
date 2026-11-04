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
    IReadOnlyList<string> Modules,
    IReadOnlyList<NotificationFolderCountDto> Folders,
    int UnseenCount,
    int? LatestNotificationId,
    string? ListVersion = null);

public sealed record NotificationProjectOptionDto(int Id, string Label, bool IsMuted);

public sealed record NotificationFolderCountDto(
    string Key,
    int TotalCount,
    int UnreadCount);

public sealed record NotificationIdsRequest(IReadOnlyCollection<int>? Ids);

public sealed record NotificationMutationDto(
    IReadOnlyList<int> NotificationIds,
    bool IsRead,
    DateTime? ReadUtc,
    DateTime? SeenUtc,
    bool AppliesToAll,
    int AffectedCount,
    int UnreadCount,
    int UnseenCount);

public sealed record NotificationSeenDto(
    IReadOnlyList<int> NotificationIds,
    DateTime SeenUtc,
    int AffectedCount,
    bool AppliesToAll,
    int UnseenCount);

/// <summary>
/// Marks the recipient's unseen notifications as seen. When <see cref="UpToId"/> is supplied only
/// notifications up to that identifier are affected, so an item that arrives while the bell is
/// opening is not marked seen before it has been rendered.
/// </summary>
public sealed record NotificationSeenAllRequest(int? UpToId);

public sealed record NotificationProjectMuteDto(
    int ProjectId,
    bool IsMuted,
    IReadOnlyList<int> ChangedNotificationIds,
    int UnreadCount,
    int UnseenCount);
