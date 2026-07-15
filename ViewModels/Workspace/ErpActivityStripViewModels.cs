namespace ProjectManagement.ViewModels.Workspace;

public sealed record ErpActivityDayVm(
    DateOnly Date,
    int Level,
    bool IsWorkingDay,
    bool IsMonitored,
    bool IsHistoricalAudit,
    string Tooltip,
    bool IsToday)
{
    public bool HasActivity => Level > 0;
}

public sealed class ErpActivityStripVm
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public IReadOnlyList<ErpActivityDayVm> Days { get; init; } = Array.Empty<ErpActivityDayVm>();
    public int ActiveWorkingDays { get; init; }
    public int MonitoredWorkingDays { get; init; }
    public DateOnly? LastActiveDate { get; init; }

    public DateOnly? MonitoringAvailableFrom => Days
        .Where(day => day.IsMonitored)
        .Select(day => (DateOnly?)day.Date)
        .Min();

    public ErpActivityDayVm? LastActiveDay => Days
        .Where(day => day.HasActivity)
        .OrderByDescending(day => day.Date)
        .FirstOrDefault();

    public string LastActivityTypeLabel => LastActiveDay?.Level switch
    {
        3 => "Operational activity",
        2 => "Interactive ERP use",
        1 => "Navigation or read-only use",
        _ => "No recorded activity"
    };

    public string MonitoringAvailabilityLabel => MonitoringAvailableFrom.HasValue
        ? $"Monitoring available from {MonitoringAvailableFrom.Value:dd MMM yyyy}"
        : "Monitoring has not started";

    public string PeriodLabel => Days.Count == 1
        ? "Last day"
        : $"Last {Days.Count} days";

    public string ActivitySummary => MonitoredWorkingDays == 0
        ? "Monitoring has not started"
        : $"Active on {ActiveWorkingDays} of {MonitoredWorkingDays} monitored working day{(MonitoredWorkingDays == 1 ? string.Empty : "s")}";

    public string LastActiveLabel
    {
        get
        {
            if (!LastActiveDate.HasValue)
            {
                return "No recorded activity";
            }

            if (LastActiveDate.Value == EndDate)
            {
                return "Last active today";
            }

            if (LastActiveDate.Value == EndDate.AddDays(-1))
            {
                return "Last active yesterday";
            }

            return $"Last active {LastActiveDate.Value:dd MMM}";
        }
    }
}

public sealed class ErpActivityStripRenderVm
{
    public ErpActivityStripVm Activity { get; init; } = new();
    public string Title { get; init; } = "ERP activity";
    public string Variant { get; init; } = "header";
    public bool ShowTitle { get; init; } = true;
    public bool ShowLegend { get; init; }
    public bool ShowSummary { get; init; } = true;
}
