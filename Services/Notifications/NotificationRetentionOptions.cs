using System;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationRetentionOptions
{
    private static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromHours(1);

    public TimeSpan SweepInterval { get; set; } = DefaultSweepInterval;

    public TimeSpan? MaxAge { get; set; }

    public int? MaxPerUser { get; set; }

    public TimeSpan? CompletedDispatchMaxAge { get; set; } = TimeSpan.FromDays(14);

    public TimeSpan? DeadLetterMaxAge { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Unread informational notifications (not marked "action required") older than this are
    /// marked read automatically, so a returning user does not face a stale unread backlog.
    /// Rows are kept; only read state changes. Null or zero disables it.
    /// </summary>
    public TimeSpan? AutoReadInformationalAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Unread action-required notifications older than this are marked read automatically. Until
    /// then they are exempt from <see cref="MaxAge"/> and <see cref="MaxPerUser"/> deletion, so a
    /// pending approval is never silently removed. Null or zero disables both auto-read and the
    /// exemption.
    /// </summary>
    public TimeSpan? AutoReadActionRequiredAfter { get; set; } = TimeSpan.FromDays(30);

    internal TimeSpan GetSweepIntervalOrDefault()
        => SweepInterval > TimeSpan.Zero ? SweepInterval : DefaultSweepInterval;

    internal bool IsRetentionEnabled()
        => (MaxAge.HasValue && MaxAge.Value > TimeSpan.Zero)
           || (MaxPerUser.HasValue && MaxPerUser.Value > 0)
           || (CompletedDispatchMaxAge.HasValue && CompletedDispatchMaxAge.Value > TimeSpan.Zero)
           || (DeadLetterMaxAge.HasValue && DeadLetterMaxAge.Value > TimeSpan.Zero)
           || IsEnabled(AutoReadInformationalAfter)
           || IsEnabled(AutoReadActionRequiredAfter);

    internal static bool IsEnabled(TimeSpan? value)
        => value.HasValue && value.Value > TimeSpan.Zero;
}
