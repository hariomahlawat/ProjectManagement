namespace ProjectManagement.Models.Notifications;

public sealed class UserProjectMute
{
    public string UserId { get; set; } = string.Empty;

    public int ProjectId { get; set; }
}
