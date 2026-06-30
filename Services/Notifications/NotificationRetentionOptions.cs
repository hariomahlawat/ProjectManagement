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

    internal TimeSpan GetSweepIntervalOrDefault()
        => SweepInterval > TimeSpan.Zero ? SweepInterval : DefaultSweepInterval;

    internal bool IsRetentionEnabled()
        => (MaxAge.HasValue && MaxAge.Value > TimeSpan.Zero)
           || (MaxPerUser.HasValue && MaxPerUser.Value > 0)
           || (CompletedDispatchMaxAge.HasValue && CompletedDispatchMaxAge.Value > TimeSpan.Zero)
           || (DeadLetterMaxAge.HasValue && DeadLetterMaxAge.Value > TimeSpan.Zero);
}
