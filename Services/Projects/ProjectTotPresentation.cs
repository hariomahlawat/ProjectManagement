using System;
using System.Collections.Generic;
using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// The one place ToT status wording and the project-page status card are decided. Pages,
/// view components, exports and history all read from here instead of keeping their own copies.
/// </summary>
public static class ProjectTotPresentation
{
    public static string StatusLabel(ProjectTotStatus status) => status switch
    {
        ProjectTotStatus.NotRequired => "Not required",
        ProjectTotStatus.NotStarted => "Not started",
        ProjectTotStatus.InProgress => "In progress",
        ProjectTotStatus.Completed => "Completed",
        ProjectTotStatus.NotKnown => "Not known",
        _ => status.ToString()
    };

    public static IReadOnlyList<(ProjectTotStatus Status, string Label)> StatusOptions { get; } = new[]
    {
        (ProjectTotStatus.NotRequired, StatusLabel(ProjectTotStatus.NotRequired)),
        (ProjectTotStatus.NotStarted, StatusLabel(ProjectTotStatus.NotStarted)),
        (ProjectTotStatus.InProgress, StatusLabel(ProjectTotStatus.InProgress)),
        (ProjectTotStatus.Completed, StatusLabel(ProjectTotStatus.Completed)),
        (ProjectTotStatus.NotKnown, StatusLabel(ProjectTotStatus.NotKnown))
    };

    /// <summary>
    /// Whether a status may be saved: only the ones the forms offer (<see cref="StatusOptions"/>).
    /// A status is added here only after every running instance can read it (Not known: read
    /// from redesign PR 3, offered from PR 4; docs/ux/tot.md).
    /// </summary>
    public static bool IsRecordable(ProjectTotStatus status)
    {
        foreach (var option in StatusOptions)
        {
            if (option.Status == status)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Formats a stored ToT date at its precision; rows saved before precision was tracked show
    /// the full date (see <see cref="PartialDateDisplay.Format(DateOnly, PartialDatePrecision)"/>).
    /// </summary>
    public static string FormatDate(DateOnly value, PartialDatePrecision precision)
        => PartialDateDisplay.Format(value, precision);

    /// <summary>A full ToT date (MET, first production model), in the module's one style: "14 Mar 2026".</summary>
    public static string? FormatDay(DateOnly? value)
        => value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>A UTC moment shown in IST, in the module's one style: "14 Mar 2026, 10:30".</summary>
    public static string? FormatMoment(DateTime? utc)
        => utc is { } value
            ? ProjectManagement.Infrastructure.IstClock.ToIst(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)
            : null;

    public static ProjectTotCard BuildCard(
        ProjectTotStatus? status,
        DateOnly? startedOn,
        PartialDatePrecision startPrecision,
        DateOnly? completedOn,
        PartialDatePrecision completionPrecision,
        ProjectTotStatus? pendingProposedStatus,
        bool canManage)
    {
        if (pendingProposedStatus.HasValue)
        {
            return new ProjectTotCard("Approval pending", $"Proposed: {StatusLabel(pendingProposedStatus.Value)}", "info");
        }

        if (!status.HasValue)
        {
            return new ProjectTotCard("Not recorded", canManage ? "Record ToT position" : "No ToT position recorded", "warning");
        }

        var title = status.Value == ProjectTotStatus.Completed ? "ToT completed" : StatusLabel(status.Value);
        var summary = status.Value switch
        {
            ProjectTotStatus.NotRequired => "No ToT action required",
            ProjectTotStatus.NotStarted => "ToT action pending",
            ProjectTotStatus.InProgress when startedOn.HasValue => $"Started {FormatDate(startedOn.Value, startPrecision)}",
            ProjectTotStatus.InProgress => "ToT is in progress",
            ProjectTotStatus.Completed when completedOn.HasValue => $"Completed {FormatDate(completedOn.Value, completionPrecision)}",
            ProjectTotStatus.Completed => "ToT marked completed",
            ProjectTotStatus.NotKnown => "Not known whether ToT took place",
            _ => "Record ToT position"
        };
        var tone = status.Value switch
        {
            ProjectTotStatus.Completed => "positive",
            ProjectTotStatus.InProgress => "info",
            ProjectTotStatus.NotRequired => "neutral",
            ProjectTotStatus.NotKnown => "neutral",
            _ => "warning"
        };

        return new ProjectTotCard(title, summary, tone);
    }
}

public sealed record ProjectTotCard(string Title, string Summary, string Tone);

/// <summary>
/// Turns the year/month/day boxes on every ToT form into stored dates, with the same rules
/// and messages on the tracker, the Overview drawer and the Edit page.
/// </summary>
public static class ProjectTotDateInput
{
    public const string StartField = "StartYear";
    public const string CompletionField = "CompletionYear";

    public static ProjectTotDateResolution Resolve(
        ProjectTotStatus status,
        PartialDateInput start,
        PartialDateInput completion)
    {
        var startPrecision = start.GetPrecision();
        var completionPrecision = completion.GetPrecision();
        DateOnly? startedOn = null;
        DateOnly? completedOn = null;
        var errors = new List<ProjectTotDateError>();

        switch (status)
        {
            case ProjectTotStatus.InProgress:
                if (PartialDateHelper.TryToStartDate(start, out var inProgressStart, out var startError))
                {
                    startedOn = inProgressStart;
                }
                else
                {
                    errors.Add(new(StartField, startError ?? "Start date is required when ToT is in progress."));
                }

                if (completionPrecision != PartialDatePrecision.None)
                {
                    errors.Add(new(CompletionField, "Completion must be empty while ToT is in progress."));
                }

                break;

            case ProjectTotStatus.Completed:
                if (PartialDateHelper.TryToCompletionDate(completion, out var completionDate, out var completionError))
                {
                    completedOn = completionDate;
                }
                else
                {
                    errors.Add(new(CompletionField, completionError ?? "Completion date is required when ToT is completed."));
                }

                if (startPrecision != PartialDatePrecision.None)
                {
                    if (PartialDateHelper.TryToStartDate(start, out var completedStart, out var completedStartError))
                    {
                        startedOn = completedStart;
                    }
                    else
                    {
                        errors.Add(new(StartField, completedStartError ?? "Invalid start date."));
                    }
                }

                break;

            case ProjectTotStatus.NotStarted:
            case ProjectTotStatus.NotRequired:
            case ProjectTotStatus.NotKnown:
                if (startPrecision != PartialDatePrecision.None)
                {
                    errors.Add(new(StartField, "Start date must be empty unless ToT has started."));
                }

                if (completionPrecision != PartialDatePrecision.None)
                {
                    errors.Add(new(CompletionField, "Completion date must be empty unless ToT is completed."));
                }

                break;

            default:
                errors.Add(new(string.Empty, "Select a valid Transfer of Technology status."));
                break;
        }

        return new ProjectTotDateResolution(startedOn, startPrecision, completedOn, completionPrecision, errors);
    }
}

public sealed record ProjectTotDateError(string Field, string Message);

public sealed record ProjectTotDateResolution(
    DateOnly? StartedOn,
    PartialDatePrecision StartDatePrecision,
    DateOnly? CompletedOn,
    PartialDatePrecision CompletionDatePrecision,
    IReadOnlyList<ProjectTotDateError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
