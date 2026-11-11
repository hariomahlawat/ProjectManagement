using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>Where the "days at the current stage" figure comes from (docs/ux/it-projects-redesign.md).</summary>
public enum ItProjectTimeBasis
{
    /// <summary>The completion date of the stage just before the current one. The real figure.</summary>
    StageDate = 0,

    /// <summary>That stage has no date; the day a stage save moved the case to the current stage.</summary>
    RecordedMove = 1,

    /// <summary>Neither date is known.</summary>
    NotRecorded = 2,

    /// <summary>No stage is complete yet; counted from the day the case was added.</summary>
    SinceAdded = 3,

    /// <summary>Delivered or dropped: no current stage to wait at.</summary>
    Closed = 4
}

/// <summary>Ageing colour of the days at the current stage. Only <see cref="ItProjectTimeBasis.StageDate"/> figures get one.</summary>
public enum ItProjectAgeBand
{
    None = 0,
    Normal = 1,
    Amber = 2,
    Red = 3
}

/// <summary>Days from the previous stage's completion to this stage's; null when either date is missing.</summary>
public sealed record ItProjectStageGap(string StageCode, int? Days);

/// <summary>
/// The time facts of one IT project, resolved once here and projected by the board, the project
/// page and later exports. Never stored.
/// </summary>
public sealed record ItProjectTiming(
    ItProjectTimeBasis Basis,
    DateOnly? Since,
    int? DaysAtStage,
    ItProjectAgeBand Band,
    IReadOnlyList<ItProjectStageGap> Gaps,
    DateOnly? StartedOn,
    int? ElapsedDays,
    bool HasMissingDates,
    string? StartedStageCode = null)
{
    /// <summary>
    /// Whether <see cref="ElapsedDays"/> runs from the first stage. When the earliest stages have
    /// no date it runs from the first dated one (<see cref="StartedStageCode"/>) and is shorter
    /// than the case really took, so it must not be called "end to end".
    /// </summary>
    public bool ElapsedIsEndToEnd => StartedStageCode is null || string.Equals(StartedStageCode, ItProjectStages.Ppp, StringComparison.OrdinalIgnoreCase);

    /// <summary>Owner decision 8 Nov 2026: up to 30 days normal, 31 to 60 amber, over 60 red, for every stage but Delivery.</summary>
    public const int NormalUpToDays = 30;

    public const int AmberUpToDays = 60;

    /// <summary>
    /// Owner decision 11 Nov 2026: Delivery and ATP waits on the supplier after the SO, which
    /// normally takes longer, so it is normal up to 60 days, amber 61 to 90, red over 90.
    /// </summary>
    public const int DeliveryNormalUpToDays = 60;

    public const int DeliveryAmberUpToDays = 90;

    public static ItProjectAgeBand BandOf(int days) => BandOf(days, stageCode: null);

    /// <summary>The ageing band of <paramref name="days"/> at the stage the case waits at.</summary>
    public static ItProjectAgeBand BandOf(int days, string? stageCode)
    {
        var (normal, amber) = LimitsFor(stageCode);
        return days <= normal ? ItProjectAgeBand.Normal
            : days <= amber ? ItProjectAgeBand.Amber
            : ItProjectAgeBand.Red;
    }

    /// <summary>The last normal day and the last amber day at a stage, for the bands and the legend.</summary>
    public static (int NormalUpTo, int AmberUpTo) LimitsFor(string? stageCode)
        => string.Equals(stageCode, ItProjectStages.Delivery, StringComparison.OrdinalIgnoreCase)
            ? (DeliveryNormalUpToDays, DeliveryAmberUpToDays)
            : (NormalUpToDays, AmberUpToDays);

    /// <param name="project">The project with its stage rows loaded.</param>
    /// <param name="today">Today in India Standard Time.</param>
    /// <param name="toLocalDate">Converts a stored UTC time to its IST calendar date.</param>
    public static ItProjectTiming Resolve(ItProject project, DateOnly today, Func<DateTime, DateOnly> toLocalDate)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(toLocalDate);

        var stages = project.Stages.OrderBy(stage => stage.SortOrder).ToList();
        var counted = stages.Where(stage => !stage.IsSkipped).ToList();
        var status = ItProjectStatus.Resolve(project);

        // Gaps: each completed stage against the stage just before it. A missing date on either
        // side leaves the gap blank; an earlier stage's date is never borrowed.
        var gaps = new List<ItProjectStageGap>(counted.Count);
        for (var index = 0; index < counted.Count; index++)
        {
            var stage = counted[index];
            int? days = null;
            if (index > 0 && stage.IsCompleted && stage.CompletedOn is { } on && counted[index - 1].CompletedOn is { } previous)
            {
                days = on.DayNumber - previous.DayNumber;
            }

            gaps.Add(new ItProjectStageGap(stage.StageCode, days));
        }

        var started = counted.FirstOrDefault(stage => stage.IsCompleted && stage.CompletedOn.HasValue);
        var startedOn = started?.CompletedOn;
        var startedCode = started?.StageCode;
        var hasMissingDates = counted.Any(stage => stage.IsCompleted && !stage.CompletedOn.HasValue);

        DateOnly? end = status.Kind switch
        {
            ItProjectStatusKind.Completed => counted.LastOrDefault()?.CompletedOn,
            ItProjectStatusKind.Dropped => project.DroppedOn,
            _ => today
        };
        int? elapsed = startedOn is { } start && end is { } finish ? Math.Max(0, finish.DayNumber - start.DayNumber) : null;

        if (status.Kind is ItProjectStatusKind.Completed or ItProjectStatusKind.Dropped)
        {
            return new ItProjectTiming(ItProjectTimeBasis.Closed, null, null, ItProjectAgeBand.None, gaps, startedOn, elapsed, hasMissingDates, startedCode);
        }

        if (status.Kind == ItProjectStatusKind.NotStarted)
        {
            var added = toLocalDate(project.CreatedAtUtc);
            return new ItProjectTiming(ItProjectTimeBasis.SinceAdded, added, DaysFrom(added, today), ItProjectAgeBand.None, gaps, startedOn, elapsed, hasMissingDates, startedCode);
        }

        var currentIndex = counted.FindIndex(stage => string.Equals(stage.StageCode, status.CurrentStageCode, StringComparison.OrdinalIgnoreCase));
        var previousStage = currentIndex > 0 ? counted[currentIndex - 1] : null;

        if (previousStage?.CompletedOn is { } closedOn)
        {
            var days = DaysFrom(closedOn, today);
            return new ItProjectTiming(ItProjectTimeBasis.StageDate, closedOn, days, BandOf(days, status.CurrentStageCode), gaps, startedOn, elapsed, hasMissingDates, startedCode);
        }

        if (project.StageMovedAtUtc is { } movedAtUtc)
        {
            var moved = toLocalDate(movedAtUtc);
            return new ItProjectTiming(ItProjectTimeBasis.RecordedMove, moved, DaysFrom(moved, today), ItProjectAgeBand.None, gaps, startedOn, elapsed, hasMissingDates, startedCode);
        }

        return new ItProjectTiming(ItProjectTimeBasis.NotRecorded, null, null, ItProjectAgeBand.None, gaps, startedOn, elapsed, hasMissingDates, startedCode);
    }

    private static int DaysFrom(DateOnly from, DateOnly today) => Math.Max(0, today.DayNumber - from.DayNumber);
}
