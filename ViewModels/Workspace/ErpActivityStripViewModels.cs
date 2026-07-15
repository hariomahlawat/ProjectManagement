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
    /// <summary>
    /// Activity that was captured after comprehensive personal usage monitoring became available.
    /// Historical audit records are deliberately excluded from adoption calculations.
    /// </summary>
    public bool HasActivity => IsMonitored && !IsHistoricalAudit && Level > 0;

    public bool HasHistoricalActivity => IsHistoricalAudit;

    public bool IsNonWorkingWithoutActivity => IsMonitored && !IsWorkingDay && !HasActivity;

    public string StateLabel => !IsMonitored
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

    public string CellStateClass => !IsMonitored
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
    public DateOnly? MonitoringStartedOn { get; init; }

    public int OperationalActionDays => Days.Count(day => day.IsMonitored && !day.IsHistoricalAudit && day.Level == 3);

    public int InteractiveOrHigherDays => Days.Count(day => day.IsMonitored && !day.IsHistoricalAudit && day.Level >= 2);

    public int HistoricalAuditDays => Days.Count(day => day.IsHistoricalAudit);

    public DateOnly? MonitoringAvailableFrom => Days
        .Where(day => day.IsMonitored)
        .Select(day => (DateOnly?)day.Date)
        .Min();

    public ErpActivityDayVm? LastActiveDay => Days
        .Where(day => day.HasActivity)
        .OrderByDescending(day => day.Date)
        .FirstOrDefault();

    public string LastActivityTypeLabel => LastActiveDay?.StateLabel ?? "No monitored activity";

    public string MonitoringAvailabilityLabel
    {
        get
        {
            var monitoringStart = MonitoringStartedOn ?? MonitoringAvailableFrom;
            return monitoringStart.HasValue
                ? $"Monitoring available from {monitoringStart.Value:dd MMM yyyy}"
                : "Monitoring has not started";
        }
    }

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
                return "No monitored activity";
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

    public bool HasHistoricalAudits => HistoricalAuditDays > 0;

    public int HistoricalAuditDays => Year.HistoricalAuditDays;

    public string HistoricalAuditSummary => HistoricalAuditDays == 1
        ? "A historical ERP record was found on 1 date before comprehensive monitoring was available. It is excluded from the activity scale and adoption metrics."
        : $"Historical ERP records were found on {HistoricalAuditDays} dates before comprehensive monitoring was available. They are excluded from the activity scale and adoption metrics.";

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
