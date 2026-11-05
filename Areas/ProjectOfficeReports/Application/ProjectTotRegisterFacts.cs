using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Services.Projects;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// The ToT facts of one project, resolved once for the ToT page, the drawer, the export and the
/// Command desk. Text a page shows is formatted here so every surface says the same thing.
/// </summary>
public sealed record ProjectTotRegisterFacts(
    int ProjectId,
    string ProjectName,
    ProjectTotStatus Status,
    string StatusLabel,
    int? ProjectCompletedYear,
    int? YearsSinceProjectCompleted,
    bool IsLongWaiting,
    string? Started,
    string? Completed,
    int? YearsFromProjectToTot,
    ProjectTotPendingChange? Pending,
    ProjectTotLastDecision? LastDecision);

/// <summary>A request waiting for the HoD: only the fields it changes, now against proposed.</summary>
public sealed record ProjectTotPendingChange(
    ProjectTotStatus ProposedStatus,
    IReadOnlyList<ProjectTotFieldChange> Changes,
    string? SubmittedBy,
    DateTime? SubmittedOnUtc);

public sealed record ProjectTotFieldChange(string Field, string Now, string Proposed);

/// <summary>The latest decided request. Its reason lives in the ToT history (ProjectAudits).</summary>
public sealed record ProjectTotLastDecision(
    ProjectTotRequestDecisionState State,
    string? DecidedBy,
    DateTime? DecidedOnUtc);

/// <summary>
/// The position over a set of projects. "Need ToT" is every project except Not required; Not
/// started includes projects with no ToT record yet.
/// </summary>
public sealed record ProjectTotPosition(
    int NeedTot,
    int Completed,
    int InProgress,
    int NotStarted,
    int NotRequired,
    int WaitingForDecision,
    int LongWaiting,
    int NotKnown = 0)
{
    public static ProjectTotPosition Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Completed as a whole percentage of Need ToT; null when nothing needs ToT.</summary>
    public int? CompletedPercent => NeedTot == 0
        ? null
        : (int)Math.Round(Completed * 100m / NeedTot, MidpointRounding.AwayFromZero);
}

public sealed record ProjectTotRegister(
    IReadOnlyList<ProjectTotRegisterFacts> Projects,
    ProjectTotPosition Position);

public static class ProjectTotRegisterFactsBuilder
{
    /// <summary>
    /// A project is long waiting when ToT has not started and the project was completed more
    /// than this many years ago, counted in calendar years (owner decision, 5 Nov 2026).
    /// </summary>
    public const int LongWaitingYears = 5;

    public static ProjectTotRegister Build(IReadOnlyList<ProjectTotTrackerRow>? rows, DateOnly today)
    {
        var projects = (rows ?? Array.Empty<ProjectTotTrackerRow>())
            .Select(row => BuildFacts(row, today))
            .ToList();

        return new ProjectTotRegister(projects, BuildPosition(projects));
    }

    public static ProjectTotRegisterFacts BuildFacts(ProjectTotTrackerRow row, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);

        var status = row.TotStatus ?? ProjectTotStatus.NotStarted;
        var projectYear = row.ProjectCompletedOn?.Year ?? row.ProjectCompletedYear;
        int? yearsSince = projectYear is { } year ? Math.Max(0, today.Year - year) : null;
        var isLongWaiting = status == ProjectTotStatus.NotStarted && yearsSince > LongWaitingYears;

        int? yearsToTot = status == ProjectTotStatus.Completed && row.TotCompletedOn is { } totDone && projectYear is { } projectDone
            ? totDone.Year - projectDone
            : null;

        return new ProjectTotRegisterFacts(
            row.ProjectId,
            row.ProjectName,
            status,
            ProjectTotPresentation.StatusLabel(status),
            projectYear,
            yearsSince,
            isLongWaiting,
            FormatPartial(row.TotStartedOn, row.TotStartDatePrecision),
            FormatPartial(row.TotCompletedOn, row.TotCompletionDatePrecision),
            yearsToTot,
            BuildPending(row, status),
            BuildLastDecision(row));
    }

    public static ProjectTotPosition BuildPosition(IReadOnlyList<ProjectTotRegisterFacts> projects)
    {
        if (projects.Count == 0)
        {
            return ProjectTotPosition.Empty;
        }

        var notRequired = projects.Count(p => p.Status == ProjectTotStatus.NotRequired);
        return new ProjectTotPosition(
            NeedTot: projects.Count - notRequired,
            Completed: projects.Count(p => p.Status == ProjectTotStatus.Completed),
            InProgress: projects.Count(p => p.Status == ProjectTotStatus.InProgress),
            NotStarted: projects.Count(p => p.Status == ProjectTotStatus.NotStarted),
            NotRequired: notRequired,
            WaitingForDecision: projects.Count(p => p.Pending is not null),
            LongWaiting: projects.Count(p => p.IsLongWaiting),
            NotKnown: projects.Count(p => p.Status == ProjectTotStatus.NotKnown));
    }

    private static ProjectTotPendingChange? BuildPending(ProjectTotTrackerRow row, ProjectTotStatus status)
    {
        if (row.RequestState != ProjectTotRequestDecisionState.Pending)
        {
            return null;
        }

        var proposedStatus = row.RequestedStatus ?? status;
        var changes = new List<ProjectTotFieldChange>();

        void Compare(string field, string? now, string? proposed)
        {
            var nowText = now ?? "—";
            var proposedText = proposed ?? "—";
            if (!string.Equals(nowText, proposedText, StringComparison.Ordinal))
            {
                changes.Add(new ProjectTotFieldChange(field, nowText, proposedText));
            }
        }

        Compare("Status", ProjectTotPresentation.StatusLabel(status), ProjectTotPresentation.StatusLabel(proposedStatus));
        Compare("Started",
            FormatPartial(row.TotStartedOn, row.TotStartDatePrecision),
            FormatPartial(row.RequestedStartedOn, row.RequestedStartDatePrecision));
        Compare("Completed",
            FormatPartial(row.TotCompletedOn, row.TotCompletionDatePrecision),
            FormatPartial(row.RequestedCompletedOn, row.RequestedCompletionDatePrecision));
        Compare("MET details", Blank(row.TotMetDetails), Blank(row.RequestedMetDetails));
        Compare("MET completed",
            ProjectTotPresentation.FormatDay(row.TotMetCompletedOn),
            ProjectTotPresentation.FormatDay(row.RequestedMetCompletedOn));
        Compare("First production model",
            YesNo(row.TotFirstProductionModelManufactured),
            YesNo(row.RequestedFirstProductionModelManufactured));
        Compare("Manufactured on",
            ProjectTotPresentation.FormatDay(row.TotFirstProductionModelManufacturedOn),
            ProjectTotPresentation.FormatDay(row.RequestedFirstProductionModelManufacturedOn));

        return new ProjectTotPendingChange(proposedStatus, changes, row.RequestedBy, row.RequestedOnUtc);
    }

    private static ProjectTotLastDecision? BuildLastDecision(ProjectTotTrackerRow row)
        => row.RequestState is ProjectTotRequestDecisionState.Approved or ProjectTotRequestDecisionState.Rejected
            ? new ProjectTotLastDecision(row.RequestState.Value, row.DecidedBy, row.DecidedOnUtc)
            : null;

    private static string? FormatPartial(DateOnly? value, PartialDatePrecision precision)
        => value is { } date ? ProjectTotPresentation.FormatDate(date, precision) : null;

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? YesNo(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        _ => null
    };
}
