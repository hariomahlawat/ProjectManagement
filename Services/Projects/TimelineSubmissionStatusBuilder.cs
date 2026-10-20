using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// The facts the Timeline status line is built from. Every value is already
/// scoped to what the viewer may see: their own plan state, the project's Actual
/// correction proposal only for the user who may submit one, and their own stage
/// requests.
/// </summary>
public sealed record TimelineSubmissionStatusInput
{
    public DateTimeOffset Now { get; init; }

    public PlanEditorStateVm? Plan { get; init; }

    public bool IncludeActualCorrections { get; init; }

    public ActualProposalSummaryVm? PendingActualCorrection { get; init; }

    public ActualCorrectionRevision? LatestActualCorrection { get; init; }

    public IReadOnlyList<StageRequestOutcomeVm> StageRequests { get; init; } = Array.Empty<StageRequestOutcomeVm>();

    public IReadOnlyDictionary<string, StageStatus> CurrentStageStatuses { get; init; } =
        new Dictionary<string, StageStatus>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Builds the per-category status line (PO Timeline discovery E.1): pending items,
/// items returned with the reviewer's note, and items approved in the last few
/// days. Presentation only; it never decides or changes anything.
/// </summary>
public static class TimelineSubmissionStatusBuilder
{
    /// <summary>A returned item stays on the line this long, or until it is acted on.</summary>
    public static readonly TimeSpan ReturnedWindow = TimeSpan.FromDays(30);

    /// <summary>An approval is mentioned briefly, then the official dates speak for themselves.</summary>
    public static readonly TimeSpan ApprovedWindow = TimeSpan.FromDays(7);

    internal const int MaxStageEntries = 3;

    private const string PlanEditorTarget = "#offcanvasPlanEdit";
    private const string ActualEditorTarget = "#offcanvasActualDates";

    public static TimelineSubmissionStatusVm Build(TimelineSubmissionStatusInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = new List<TimelineSubmissionLineVm>();
        AddLine(lines, TimelineSubmissionStatusVm.PlannedDatesCategory, PlanEntries(input));
        if (input.IncludeActualCorrections)
        {
            AddLine(lines, TimelineSubmissionStatusVm.ActualDatesCategory, ActualEntries(input));
        }

        var stageEntries = StageEntries(input).ToList();
        if (stageEntries.Count > 0)
        {
            lines.Add(new TimelineSubmissionLineVm(
                TimelineSubmissionStatusVm.StageUpdatesCategory,
                stageEntries.Take(MaxStageEntries).ToArray(),
                stageEntries.Count - Math.Min(stageEntries.Count, MaxStageEntries)));
        }

        return lines.Count == 0 ? TimelineSubmissionStatusVm.Empty : new TimelineSubmissionStatusVm(lines);
    }

    private static void AddLine(List<TimelineSubmissionLineVm> lines, string category, TimelineSubmissionEntryVm? entry)
    {
        if (entry is not null)
        {
            lines.Add(new TimelineSubmissionLineVm(category, new[] { entry }));
        }
    }

    // SECTION: Planned dates
    private static TimelineSubmissionEntryVm? PlanEntries(TimelineSubmissionStatusInput input)
    {
        var plan = input.Plan;
        if (plan is null)
        {
            return null;
        }

        if (plan.HasPendingSubmission && plan.PendingOwnedByCurrentUser)
        {
            return new TimelineSubmissionEntryVm(
                TimelineSubmissionState.Pending,
                plan.PendingSubmittedOn is DateTimeOffset submittedOn
                    ? $"Awaiting HoD approval (submitted {Date(submittedOn)})."
                    : "Awaiting HoD approval.",
                ActionLabel: "View",
                OffcanvasTarget: PlanEditorTarget);
        }

        if (plan.RejectedOn is DateTimeOffset rejectedOn && Within(input.Now, rejectedOn, ReturnedWindow))
        {
            return new TimelineSubmissionEntryVm(
                TimelineSubmissionState.Returned,
                $"Returned on {Date(rejectedOn)}.",
                Clean(plan.RejectionNote),
                ActionLabel: "Review",
                OffcanvasTarget: PlanEditorTarget);
        }

        // A plan approval is already shown by the card's "Approved on" badge.
        return null;
    }

    // SECTION: Actual dates
    private static TimelineSubmissionEntryVm? ActualEntries(TimelineSubmissionStatusInput input)
    {
        if (input.PendingActualCorrection is { } pending)
        {
            return new TimelineSubmissionEntryVm(
                TimelineSubmissionState.Pending,
                $"Awaiting HoD or Admin approval (submitted {Date(pending.SubmittedOn)}).",
                ActionLabel: "View",
                OffcanvasTarget: ActualEditorTarget);
        }

        var latest = input.LatestActualCorrection;
        if (latest?.ClosedOn is not DateTimeOffset closedOn)
        {
            return null;
        }

        if (latest.Status == ActualCorrectionRevisionStatus.Rejected && Within(input.Now, closedOn, ReturnedWindow))
        {
            return new TimelineSubmissionEntryVm(
                TimelineSubmissionState.Returned,
                $"Correction returned on {Date(closedOn)}.",
                Clean(latest.DecisionNote),
                ActionLabel: "Review",
                OffcanvasTarget: ActualEditorTarget);
        }

        if (latest.Status == ActualCorrectionRevisionStatus.Approved && Within(input.Now, closedOn, ApprovedWindow))
        {
            return new TimelineSubmissionEntryVm(
                TimelineSubmissionState.Approved,
                $"Correction approved on {Date(closedOn)}.");
        }

        return null;
    }

    // SECTION: Stage updates
    private static IEnumerable<TimelineSubmissionEntryVm> StageEntries(TimelineSubmissionStatusInput input)
    {
        var returned = new List<TimelineSubmissionEntryVm>();
        var pending = new List<TimelineSubmissionEntryVm>();
        var approved = new List<TimelineSubmissionEntryVm>();

        foreach (var request in input.StageRequests)
        {
            var subject = $"{ActionNoun(request.RequestedStatus)} of {request.StageName}";
            switch (request.DecisionStatus)
            {
                case "Pending":
                    pending.Add(new TimelineSubmissionEntryVm(
                        TimelineSubmissionState.Pending,
                        $"{subject} awaiting HoD approval (submitted {Date(request.RequestedOn)}).",
                        ActionLabel: "View",
                        StageCode: request.StageCode));
                    break;
                case "Rejected" when request.DecidedOn is DateTimeOffset decidedOn
                                     && Within(input.Now, decidedOn, ReturnedWindow)
                                     && !AlreadyApplies(input, request):
                    returned.Add(new TimelineSubmissionEntryVm(
                        TimelineSubmissionState.Returned,
                        $"{subject} returned on {Date(decidedOn)}.",
                        Clean(request.DecisionNote),
                        ActionLabel: "Review",
                        StageCode: request.StageCode));
                    break;
                case "Approved" when request.DecidedOn is DateTimeOffset decidedOn
                                     && Within(input.Now, decidedOn, ApprovedWindow):
                    approved.Add(new TimelineSubmissionEntryVm(
                        TimelineSubmissionState.Approved,
                        $"{subject} approved on {Date(decidedOn)}.",
                        StageCode: request.StageCode));
                    break;
            }
        }

        // What needs action first, then what is waiting, then good news.
        return returned.Concat(pending).Concat(approved);
    }

    // A return is moot once the stage already has the status that was requested.
    private static bool AlreadyApplies(TimelineSubmissionStatusInput input, StageRequestOutcomeVm request) =>
        Enum.TryParse<StageStatus>(request.RequestedStatus, ignoreCase: true, out var requested)
        && input.CurrentStageStatuses.TryGetValue(request.StageCode, out var current)
        && current == requested;

    internal static string ActionNoun(string? requestedStatus) => requestedStatus?.Trim().ToLowerInvariant() switch
    {
        "completed" => "Completion",
        "inprogress" => "Start",
        "blocked" => "Blocked status",
        "skipped" => "Skip",
        "notstarted" => "Reopening",
        _ => "Update"
    };

    private static bool Within(DateTimeOffset now, DateTimeOffset value, TimeSpan window) => now - value <= window;

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string Date(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, TimeZoneHelper.GetIst()).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}
