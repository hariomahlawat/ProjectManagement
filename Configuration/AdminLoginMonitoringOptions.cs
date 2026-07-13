namespace ProjectManagement.Configuration;

/// <summary>
/// Defines the deterministic rules and safety limits used by the administrative
/// login-monitoring workspace.
/// </summary>
public sealed class AdminLoginMonitoringOptions
{
    public const string SectionName = "AdminLoginMonitoring";

    public TimeSpan WorkdayStart { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan WorkdayEnd { get; set; } = TimeSpan.FromHours(18);

    public bool MarkWeekendsForReview { get; set; } = true;

    public int DefaultLookbackDays { get; set; } = 30;

    public int MaximumLookbackDays { get; set; } = 365;

    public int MaximumChartPoints { get; set; } = 5_000;

    public int DefaultReviewPageSize { get; set; } = 25;

    public int MaximumReviewPageSize { get; set; } = 100;
}
