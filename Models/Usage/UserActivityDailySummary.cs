using ProjectManagement.Models;

namespace ProjectManagement.Models.Usage;

/// <summary>
/// Permanent per-user, per-calendar-day activity summary. The summary preserves the
/// highest-level navigation and interactive signals after detailed time buckets are
/// removed by the bounded retention worker.
/// </summary>
public sealed class UserActivityDailySummary
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateOnly ActivityDateIst { get; set; }
    public bool HadNavigation { get; set; }
    public bool HadInteractiveHeartbeat { get; set; }
    public bool HadAdministrativeAction { get; set; }
    public bool HadOperationalAction { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int NavigationCount { get; set; }
    public int HeartbeatCount { get; set; }
    public int AdministrativeActionCount { get; set; }
    public int OperationalActionCount { get; set; }
    public ApplicationUser? User { get; set; }
}
