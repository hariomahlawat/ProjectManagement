namespace ProjectManagement.Models.Notifications;

public sealed class UserNotificationPreference
{
    public string UserId { get; set; } = string.Empty;

    public NotificationKind Kind { get; set; }

    public bool Allow { get; set; }
}
