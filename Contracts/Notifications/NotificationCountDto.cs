namespace ProjectManagement.Contracts.Notifications;

/// <summary>
/// Unread drives the Notification Centre and "mark all read"; unseen drives the bell badge, which
/// clears when the recipient opens the bell even if individual items stay unread.
/// </summary>
public sealed record NotificationCountDto(int UnreadCount, int UnseenCount);
