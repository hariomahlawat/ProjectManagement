using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// The ToT page's overview: the opening sentence over the position counts, and the completions
/// per year for the chart. Counts come from <see cref="ProjectTotRegisterFactsBuilder"/>.
/// </summary>
public static class ProjectTotOverview
{
    /// <summary>
    /// "65 of 118 completed projects have completed Transfer of Technology (55%). 53 have not
    /// started. A further 21 do not require it."
    /// </summary>
    public static string Narrative(ProjectTotPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var furtherNote = position.NotRequired switch
        {
            0 => string.Empty,
            1 => " A further 1 project does not require it.",
            _ => $" A further {position.NotRequired.ToString(CultureInfo.InvariantCulture)} projects do not require it."
        };

        if (position.NeedTot == 0)
        {
            return "No completed project needs Transfer of Technology yet." + furtherNote;
        }

        var lead = $"{position.Completed.ToString(CultureInfo.InvariantCulture)} of {Count(position.NeedTot, "completed project", "completed projects")} "
            + $"{(position.Completed == 1 ? "has" : "have")} completed Transfer of Technology ({position.CompletedPercent?.ToString(CultureInfo.InvariantCulture)}%).";

        var rest = new List<string>();
        if (position.InProgress > 0)
        {
            rest.Add($"{position.InProgress.ToString(CultureInfo.InvariantCulture)} {(position.InProgress == 1 ? "is" : "are")} in progress");
        }

        if (position.NotStarted > 0)
        {
            rest.Add($"{position.NotStarted.ToString(CultureInfo.InvariantCulture)} {(position.NotStarted == 1 ? "has" : "have")} not started");
        }

        if (position.NotKnown > 0)
        {
            rest.Add($"for {position.NotKnown.ToString(CultureInfo.InvariantCulture)} it is not known");
        }

        var middle = rest.Count switch
        {
            0 => string.Empty,
            1 => $" {Capitalise(rest[0])}.",
            _ => $" {Capitalise(string.Join(", ", rest.Take(rest.Count - 1)))} and {rest[^1]}."
        };

        return lead + middle + furtherNote;
    }

    /// <summary>
    /// One point per year from the first to the last ToT completion, so a year with no
    /// completions shows as zero instead of being left off the axis.
    /// </summary>
    public static IReadOnlyList<(int Year, int Count)> BuildYearlyCompletions(IReadOnlyList<ProjectTotTrackerRow> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var counts = projects
            .Where(p => p.TotStatus == ProjectTotStatus.Completed && p.TotCompletedOn.HasValue)
            .GroupBy(p => p.TotCompletedOn!.Value.Year)
            .ToDictionary(g => g.Key, g => g.Count());

        if (counts.Count == 0)
        {
            return Array.Empty<(int, int)>();
        }

        var first = counts.Keys.Min();
        var last = counts.Keys.Max();
        return Enumerable.Range(first, last - first + 1)
            .Select(year => (year, counts.GetValueOrDefault(year)))
            .ToList();
    }

    /// <summary>
    /// A request's change in a few words for a list: "In progress → Completed, Oct 2026" when the
    /// status changes, else the first changed field ("Completed: — → Oct 2026").
    /// </summary>
    public static string ChangeSummary(ProjectTotPendingChange pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        var status = pending.Changes.FirstOrDefault(change => change.Field == "Status");
        if (status is not null)
        {
            var date = pending.Changes.FirstOrDefault(change =>
                change.Field == (pending.ProposedStatus == ProjectTotStatus.Completed ? "Completed" : "Started"));
            return date is null || date.Proposed == "—"
                ? $"{status.Now} → {status.Proposed}"
                : $"{status.Now} → {status.Proposed}, {date.Proposed}";
        }

        var first = pending.Changes.FirstOrDefault();
        return first is null ? "No field changes" : $"{first.Field}: {first.Now} → {first.Proposed}";
    }

    /// <summary>
    /// The register's Note for a project, or null: a request waiting, how long a not-started
    /// project has waited, or how long after the project's completion ToT was completed.
    /// </summary>
    public static string? RowNote(ProjectTotRegisterFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Pending is not null)
        {
            return "Waiting for a decision";
        }

        // A rejected request is worth knowing until another one is sent; an approved one is
        // already the record, so it needs no note.
        if (facts.LastDecision is { State: ProjectTotRequestDecisionState.Rejected } rejected)
        {
            return rejected.DecidedOnUtc is { } on
                ? $"Request rejected {ProjectManagement.Infrastructure.IstClock.ToIst(DateTime.SpecifyKind(on, DateTimeKind.Utc)).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
                : "Request rejected";
        }

        if (facts.IsLongWaiting && facts.YearsSinceProjectCompleted is { } waited)
        {
            return $"Waiting {waited.ToString(CultureInfo.InvariantCulture)} years";
        }

        return facts.YearsFromProjectToTot switch
        {
            null => null,
            < 0 => "Before project completion",
            0 => "Same year as completion",
            1 => "1 year after completion",
            { } years => $"{years.ToString(CultureInfo.InvariantCulture)} years after completion"
        };
    }

    private static string Count(int count, string singular, string plural)
        => $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    private static string Capitalise(string text)
        => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
