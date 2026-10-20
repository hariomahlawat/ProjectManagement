using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.ViewModels;

/// <summary>
/// The compact "where do my submissions stand" summary in the Timeline card:
/// at most one line per category (planned dates, actual dates, stage updates),
/// each listing the viewer's pending, returned or recently approved items.
/// It is a read model only; it changes no state.
/// </summary>
public sealed class TimelineSubmissionStatusVm
{
    public const string PlannedDatesCategory = "Planned dates";
    public const string ActualDatesCategory = "Actual dates";
    public const string StageUpdatesCategory = "Stage updates";

    public static readonly TimelineSubmissionStatusVm Empty = new(Array.Empty<TimelineSubmissionLineVm>());

    public TimelineSubmissionStatusVm(IReadOnlyList<TimelineSubmissionLineVm> lines)
    {
        Lines = lines ?? Array.Empty<TimelineSubmissionLineVm>();
    }

    public IReadOnlyList<TimelineSubmissionLineVm> Lines { get; }

    public bool HasLines => Lines.Count > 0;

    /// <summary>The viewer's own pending stage updates, including any not listed.</summary>
    public int OwnPendingStageUpdateCount { get; init; }

    public bool Shows(string category, TimelineSubmissionState state) =>
        Lines.Any(line => string.Equals(line.Category, category, StringComparison.Ordinal) &&
                          line.Entries.Any(entry => entry.State == state));
}

public sealed record TimelineSubmissionLineVm(
    string Category,
    IReadOnlyList<TimelineSubmissionEntryVm> Entries,
    int HiddenCount = 0);

/// <summary>
/// One item in a line. <see cref="OffcanvasTarget"/> opens an existing editor;
/// <see cref="StageCode"/> links to the stage on the timeline.
/// </summary>
public sealed record TimelineSubmissionEntryVm(
    TimelineSubmissionState State,
    string Text,
    string? ReviewerNote = null,
    string? ActionLabel = null,
    string? OffcanvasTarget = null,
    string? StageCode = null);

public enum TimelineSubmissionState
{
    Pending,
    Returned,
    Approved
}

/// <summary>
/// The viewer's latest stage request for one stage, when it is still the latest
/// request for that stage (read by <c>ProjectTimelineReadService</c>).
/// </summary>
public sealed record StageRequestOutcomeVm(
    string StageCode,
    string StageName,
    string RequestedStatus,
    string DecisionStatus,
    DateTimeOffset RequestedOn,
    DateTimeOffset? DecidedOn,
    string? DecisionNote);
