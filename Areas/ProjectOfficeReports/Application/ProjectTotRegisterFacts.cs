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
    ProjectTotLastDecision? LastDecision,
    DateOnly? CompletedOn = null);

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
            BuildLastDecision(row),
            status == ProjectTotStatus.Completed ? row.TotCompletedOn : null);
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

    /// <summary>
    /// A pending request against the stored ToT, for one project (the project page's drawer).
    /// Uses the same comparison as the register.
    /// </summary>
    public static ProjectTotPendingChange BuildPendingChange(ProjectTot? current, ProjectTotRequest request, string? submittedBy)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = current is null
            ? new TotValues(ProjectTotStatus.NotStarted, null, PartialDatePrecision.None, null, PartialDatePrecision.None, null, null, null, null)
            : new TotValues(
                current.Status,
                current.StartedOn, current.StartDatePrecision,
                current.CompletedOn, current.CompletionDatePrecision,
                current.MetDetails, current.MetCompletedOn,
                current.FirstProductionModelManufactured, current.FirstProductionModelManufacturedOn);
        var proposed = new TotValues(
            request.ProposedStatus,
            request.ProposedStartedOn, request.ProposedStartDatePrecision,
            request.ProposedCompletedOn, request.ProposedCompletionDatePrecision,
            request.ProposedMetDetails, request.ProposedMetCompletedOn,
            request.ProposedFirstProductionModelManufactured, request.ProposedFirstProductionModelManufacturedOn);

        return new ProjectTotPendingChange(request.ProposedStatus, Compare(now, proposed), submittedBy, request.SubmittedOnUtc);
    }

    private static ProjectTotPendingChange? BuildPending(ProjectTotTrackerRow row, ProjectTotStatus status)
    {
        if (row.RequestState != ProjectTotRequestDecisionState.Pending)
        {
            return null;
        }

        var proposedStatus = row.RequestedStatus ?? status;
        var now = new TotValues(
            status,
            row.TotStartedOn, row.TotStartDatePrecision,
            row.TotCompletedOn, row.TotCompletionDatePrecision,
            row.TotMetDetails, row.TotMetCompletedOn,
            row.TotFirstProductionModelManufactured, row.TotFirstProductionModelManufacturedOn);
        var proposed = new TotValues(
            proposedStatus,
            row.RequestedStartedOn, row.RequestedStartDatePrecision,
            row.RequestedCompletedOn, row.RequestedCompletionDatePrecision,
            row.RequestedMetDetails, row.RequestedMetCompletedOn,
            row.RequestedFirstProductionModelManufactured, row.RequestedFirstProductionModelManufacturedOn);

        return new ProjectTotPendingChange(proposedStatus, Compare(now, proposed), row.RequestedBy, row.RequestedOnUtc);
    }

    private sealed record TotValues(
        ProjectTotStatus Status,
        DateOnly? StartedOn,
        PartialDatePrecision StartPrecision,
        DateOnly? CompletedOn,
        PartialDatePrecision CompletionPrecision,
        string? MetDetails,
        DateOnly? MetCompletedOn,
        bool? FirstProductionModel,
        DateOnly? FirstProductionModelOn);

    // Only the fields that differ, as the reader sees them ("—" for nothing recorded).
    private static IReadOnlyList<ProjectTotFieldChange> Compare(TotValues now, TotValues proposed)
    {
        var changes = new List<ProjectTotFieldChange>();

        void Add(string field, string? nowValue, string? proposedValue)
        {
            var nowText = nowValue ?? "—";
            var proposedText = proposedValue ?? "—";
            if (!string.Equals(nowText, proposedText, StringComparison.Ordinal))
            {
                changes.Add(new ProjectTotFieldChange(field, nowText, proposedText));
            }
        }

        Add("Status", ProjectTotPresentation.StatusLabel(now.Status), ProjectTotPresentation.StatusLabel(proposed.Status));
        Add("Started", FormatPartial(now.StartedOn, now.StartPrecision), FormatPartial(proposed.StartedOn, proposed.StartPrecision));
        Add("Completed", FormatPartial(now.CompletedOn, now.CompletionPrecision), FormatPartial(proposed.CompletedOn, proposed.CompletionPrecision));
        Add("MET details", Blank(now.MetDetails), Blank(proposed.MetDetails));
        Add("MET completed", ProjectTotPresentation.FormatDay(now.MetCompletedOn), ProjectTotPresentation.FormatDay(proposed.MetCompletedOn));
        Add("First production model", YesNo(now.FirstProductionModel), YesNo(proposed.FirstProductionModel));
        Add("Manufactured on", ProjectTotPresentation.FormatDay(now.FirstProductionModelOn), ProjectTotPresentation.FormatDay(proposed.FirstProductionModelOn));

        return changes;
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
