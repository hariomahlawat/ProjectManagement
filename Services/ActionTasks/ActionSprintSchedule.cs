using System;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

/// <summary>How a sprint's dates read in headers: its date range and where today falls (IST).</summary>
public static class ActionSprintSchedule
{
    /// <summary>"13–26 Oct 2026", "28 Sep – 12 Oct 2026" or "22 Dec 2026 – 4 Jan 2027".</summary>
    public static string DateRange(ActionSprint sprint)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        var start = sprint.StartDate.Date;
        var end = sprint.EndDate.Date;
        if (start.Year != end.Year)
        {
            return $"{start:dd MMM yyyy} – {end:dd MMM yyyy}";
        }

        return start.Month == end.Month
            ? $"{start:dd}–{end:dd MMM yyyy}"
            : $"{start:dd MMM} – {end:dd MMM yyyy}";
    }

    /// <summary>
    /// Where today falls against the sprint: "3 days left", "Ends today", "2 days past its end",
    /// "Starts in 4 days" or "Starts today"; null for a closed sprint.
    /// </summary>
    public static string? Timing(ActionSprint sprint, DateTime istToday)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        var today = istToday.Date;
        switch (sprint.Status)
        {
            case ActionSprintStatus.Closed:
                return null;
            case ActionSprintStatus.Planned:
                var toStart = (int)(sprint.StartDate.Date - today).TotalDays;
                return toStart switch
                {
                    > 1 => $"Starts in {toStart} days",
                    1 => "Starts tomorrow",
                    0 => "Starts today",
                    _ => "Start date has passed"
                };
            default:
                var left = (int)(sprint.EndDate.Date - today).TotalDays;
                return left switch
                {
                    > 1 => $"{left} days left",
                    1 => "1 day left",
                    0 => "Ends today",
                    -1 => "1 day past its end",
                    _ => $"{-left} days past its end"
                };
        }
    }

    /// <summary>Closed share of the sprint's tasks, 0–100, for a progress bar.</summary>
    public static int PercentClosed(int closed, int total)
        => total <= 0 ? 0 : (int)Math.Round(100.0 * Math.Clamp(closed, 0, total) / total);
}
