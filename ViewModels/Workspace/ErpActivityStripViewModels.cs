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

    public bool IsNonWorkingWithoutActivity => !IsWorkingDay && !HasActivity;

    public string StateLabel => IsHistoricalAudit
        ? Level switch
        {
            3 => "Historical audited operational activity",
            2 => "Historical audited administrative activity",
            _ => "Historical audited activity"
        }
        : !IsMonitored
            ? "Not monitored"
            : IsNonWorkingWithoutActivity
                ? "Non-working day"
                : Level switch
                {
                    3 => "Operational activity",
                    2 => "Interactive use",
                    1 => "Navigation or read-only use",
                    _ => "No recorded activity"
                };

    public string CellStateClass => IsHistoricalAudit
        ? $"level-{Math.Clamp(Level, 0, 3)} is-historical"
        : !IsMonitored
            ? "not-monitored"
            : IsNonWorkingWithoutActivity
                ? "non-working"
                : $"level-{Math.Clamp(Level, 0, 3)}";
}

public sealed class ErpActivityStripVm
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public IReadOnlyList<ErpActivityDayVm> Days { get; init; } = Array.Empty<ErpActivityDayVm>();
    public int ActiveWorkingDays { get; init; }
    public int MonitoredWorkingDays { get; init; }
    public DateOnly? LastActiveDate { get; init; }

    public int OperationalActionDays => Days.Count(day => day.Level == 3);

    public int InteractiveOrHigherDays => Days.Count(day => day.Level >= 2);

    public DateOnly? MonitoringAvailableFrom => Days
        .Where(day => day.IsMonitored)
        .Select(day => (DateOnly?)day.Date)
        .Min();

    public ErpActivityDayVm? LastActiveDay => Days
        .Where(day => day.HasActivity)
        .OrderByDescending(day => day.Date)
        .FirstOrDefault();

    public string LastActivityTypeLabel => LastActiveDay?.StateLabel ?? "No recorded activity";

    public string MonitoringAvailabilityLabel => MonitoringAvailableFrom.HasValue
        ? $"Monitoring available from {MonitoringAvailableFrom.Value:dd MMM yyyy}"
        : "Monitoring has not started";

    public string PeriodLabel => Days.Count == 1
        ? "Last day"
        : $"Last {Days.Count} days";

    public string DateRangeLabel => StartDate == EndDate
        ? EndDate.ToString("dd MMM yyyy")
        : $"{StartDate:dd MMM yyyy} – {EndDate:dd MMM yyyy}";

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

            return $"Last active {LastActiveDate.Value:dd MMM yyyy}";
        }
    }
}

public sealed record ErpActivityWeekVm(
    DateOnly StartDate,
    string? MonthLabel,
    IReadOnlyList<ErpActivityDayVm?> Days);

public sealed class ErpActivityYearVm
{
    public ErpActivityStripVm Year { get; init; } = new();
    public ErpActivityStripVm Recent { get; init; } = new();
    public IReadOnlyList<ErpActivityWeekVm> Weeks { get; init; } = Array.Empty<ErpActivityWeekVm>();

    public int WeekCount => Weeks.Count;

    public bool HasHistoricalAudits => Year.Days.Any(day => day.IsHistoricalAudit);

    public string RollingPeriodLabel => $"{Year.StartDate:dd MMM yyyy} – {Year.EndDate:dd MMM yyyy}";
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
