namespace ProjectManagement.Configuration;

public sealed class ErpUsageOptions
{
    public const string SectionName = "ErpUsage";

    public int BucketMinutes { get; set; } = 5;
    public int HeartbeatIntervalSeconds { get; set; } = 180;
    public int InteractiveIdleMinutes { get; set; } = 10;
    public int RetentionDays { get; set; } = 180;
    public int RegularUserThresholdPercent { get; set; } = 80;
    public int MaximumLookbackDays { get; set; } = 90;
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
