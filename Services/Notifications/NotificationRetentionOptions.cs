using System;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationRetentionOptions
{
    private static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromHours(1);

    public TimeSpan SweepInterval { get; set; } = DefaultSweepInterval;

    public TimeSpan? MaxAge { get; set; }

    public int? MaxPerUser { get; set; }

    internal TimeSpan GetSweepIntervalOrDefault()
    {
        return SweepInterval > TimeSpan.Zero ? SweepInterval : DefaultSweepInterval;
    }

    internal bool IsRetentionEnabled()
    {
        return (MaxAge.HasValue && MaxAge.Value > TimeSpan.Zero)
            || (MaxPerUser.HasValue && MaxPerUser.Value > 0);
    }
}
