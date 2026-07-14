using ProjectManagement.Models;

namespace ProjectManagement.Models.Usage;

/// <summary>
/// A bounded activity interval. Repeated requests and browser heartbeats in the same
/// user/module interval update this record instead of creating one row per event.
/// </summary>
public sealed class UserActivityBucket
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime BucketStartUtc { get; set; }
    public DateOnly ActivityDateIst { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public bool HadNavigation { get; set; }
    public bool HadInteractiveHeartbeat { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int NavigationCount { get; set; }
    public int HeartbeatCount { get; set; }
    public ApplicationUser? User { get; set; }
}
