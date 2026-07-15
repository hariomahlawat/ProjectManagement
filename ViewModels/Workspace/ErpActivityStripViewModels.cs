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

    public string PeriodLabel => Days.Count == 1
        ? "Last day"
        : $"Last {Days.Count} days";

    public string ActivitySummary
    {
        get
        {
            if (MonitoredWorkingDays <= 0)
            {
                return "Monitoring not yet available";
            }

            var dayLabel = MonitoredWorkingDays == 1 ? "working day" : "working days";
            return $"{ActiveWorkingDays} of {MonitoredWorkingDays} monitored {dayLabel} active";
        }
    }

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
