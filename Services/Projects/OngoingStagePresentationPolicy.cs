using System;
using System.Collections.Generic;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Centralises ongoing-project timeline presentation rules so the card view,
/// table view and lightweight stage counts use identical lifecycle semantics.
/// </summary>
internal static class OngoingStagePresentationPolicy
{
    internal const int DefaultProlongedDays = 90;
    internal const int DefaultWarningDays = 45;
    internal const int DevelopmentProlongedMonths = 6;
    internal const int DevelopmentWarningMonths = 3;
    internal const string ForwardStageFlow = "forward";
    internal const string ReverseStageFlow = "reverse";

    internal static string NormalizeStageFlow(string? value)
        => string.Equals(value, ForwardStageFlow, StringComparison.OrdinalIgnoreCase)
            ? ForwardStageFlow
            : ReverseStageFlow;

    internal static bool IsReverseStageFlow(string? value)
        => string.Equals(
            NormalizeStageFlow(value),
            ReverseStageFlow,
            StringComparison.Ordinal);

    /// <summary>
    /// Resolves the current lifecycle stage. An explicit in-progress stage wins;
    /// otherwise completed and skipped stages are treated as terminal and the
    /// first remaining actionable stage becomes current.
    /// </summary>
    internal static int ResolveCurrentStageIndex(IReadOnlyList<StageStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        if (statuses.Count == 0)
        {
            throw new ArgumentException("At least one workflow stage is required.", nameof(statuses));
        }

        for (var index = 0; index < statuses.Count; index++)
        {
            if (statuses[index] == StageStatus.InProgress)
            {
                return index;
            }
        }

        for (var index = 0; index < statuses.Count; index++)
        {
            if (!IsTerminal(statuses[index]))
            {
                return index;
            }
        }

        // Defensive fallback for an active project whose workflow is entirely
        // terminal. The final stage remains the stable visual anchor.
        return statuses.Count - 1;
    }

    internal static bool IsTerminal(StageStatus status)
        => status is StageStatus.Completed or StageStatus.Skipped;

    /// <summary>
    /// Determines whether the current stage is prolonged. Development uses a
    /// six-calendar-month threshold; all other stages retain the 90-day rule.
    /// </summary>
    internal static bool IsProlonged(
        string? stageCode,
        PresentStageSnapshot snapshot,
        DateOnly today)
    {
        var anchor = ResolveAgeAnchor(snapshot);
        if (!anchor.HasValue || today < anchor.Value)
        {
            return false;
        }

        return string.Equals(stageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
            ? anchor.Value.AddMonths(DevelopmentProlongedMonths) <= today
            : today.DayNumber - anchor.Value.DayNumber >= DefaultProlongedDays;
    }

    /// <summary>
    /// Supplies the table's warning state without labelling the stage prolonged.
    /// Development warns after three calendar months; other stages after 45 days.
    /// </summary>
    internal static bool IsApproachingProlonged(
        string? stageCode,
        PresentStageSnapshot snapshot,
        DateOnly today)
    {
        var anchor = ResolveAgeAnchor(snapshot);
        if (!anchor.HasValue || today < anchor.Value)
        {
            return false;
        }

        return string.Equals(stageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
            ? anchor.Value.AddMonths(DevelopmentWarningMonths) <= today
            : today.DayNumber - anchor.Value.DayNumber >= DefaultWarningDays;
    }

    private static DateOnly? ResolveAgeAnchor(PresentStageSnapshot snapshot)
        => snapshot.CurrentStageStartDate ?? snapshot.LastCompletedDate;
}
