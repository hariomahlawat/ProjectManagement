namespace ProjectManagement.Contracts.Notifications;

/// <summary>
/// Unread drives the Notification Centre and "mark all read"; unseen drives the bell badge, which
/// clears when the recipient opens the bell even if individual items stay unread.
/// ListVersion changes whenever the list could look different (a row added, deleted, read, unread
/// or seen), so a client can skip reloading an unchanged list.
/// </summary>
public sealed record NotificationCountDto(
    int UnreadCount,
    int UnseenCount,
    int? LatestNotificationId = null,
    string? ListVersion = null);
