namespace ProjectManagement.Configuration;

public sealed class ErpUsageOptions
{
    public const string SectionName = "ErpUsage";

    /// <summary>
    /// Exact UTC instant from which navigation and heartbeat monitoring became comprehensive.
    /// Historical audited actions may still be displayed, but must not be used to infer
    /// absence of read-only usage before this instant.
    /// </summary>
    public DateTimeOffset TrackingInceptionUtc { get; set; } =
        new(2026, 7, 14, 7, 30, 0, TimeSpan.Zero);

    public int BucketMinutes { get; set; } = 5;
    public int HeartbeatIntervalSeconds { get; set; } = 180;
    public int InteractiveIdleMinutes { get; set; } = 10;
    /// <summary>
    /// Retention for detailed module/time buckets. Permanent per-day summaries are not deleted.
    /// </summary>
    public int RetentionDays { get; set; } = 400;
    public int RegularUserThresholdPercent { get; set; } = 80;
    public int MaximumLookbackDays { get; set; } = 365;
    public int MaximumExportRows { get; set; } = 2000;

    public DayOfWeek[] WorkingDays { get; set; } =
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday
    };
}
