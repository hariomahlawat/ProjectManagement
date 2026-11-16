using System;
using System.Collections.Generic;
using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Services.Analytics;

/// <summary>
/// The one rule that places a project in a progress band (Pre-AoN, AoN to SO, Under development, Completed),
/// shared by Analytics › ARPP and the briefing decks' stage-wise distribution so both always agree.
/// <list type="bullet">
/// <item>Past AoN: an AoN date, or the AoN stage Completed or Skipped (with or without its date, owner,
/// 6 Nov 2026); a workflow with no AoN stage is past AoN once its current stage is beyond the Pre-AoN phase.</item>
/// <item>Supply order placed: a supply-order date, or a stage beyond the Supply Order stage (being <em>in</em>
/// the Supply Order stage is not yet an order).</item>
/// <item>A project in work with its supply order placed is Under development; without one it is AoN to SO when
/// past AoN and Pre-AoN when not. Anything the records cannot place is Not known, never guessed.</item>
/// </list>
/// </summary>
public static class ProjectProgressBands
{
    /// <param name="aonDate">The completed AoN date (<see cref="ProjectFormalUpdateFacts.AonDate"/>).</param>
    /// <param name="aonStageStatuses">The statuses of the project's AoN stage rows; empty when its workflow has none.</param>
    /// <param name="inWork">Active and not archived.</param>
    /// <param name="currentStageCode">The stage the project stands at.</param>
    public static bool IsPastAon(DateOnly? aonDate, IReadOnlyCollection<StageStatus> aonStageStatuses, bool inWork, string? currentStageCode)
    {
        ArgumentNullException.ThrowIfNull(aonStageStatuses);
        if (aonDate.HasValue)
        {
            return true;
        }

        if (aonStageStatuses.Count > 0)
        {
            return Contains(aonStageStatuses, StageStatus.Completed) || Contains(aonStageStatuses, StageStatus.Skipped);
        }

        return inWork
            && !string.IsNullOrWhiteSpace(currentStageCode)
            && StagePhases.Of(currentStageCode) != StagePhase.PreAon;
    }

    /// <summary>AoN not required: no AoN date, and the AoN stage was skipped rather than completed.</summary>
    public static bool IsAonSkipped(DateOnly? aonDate, IReadOnlyCollection<StageStatus> aonStageStatuses)
    {
        ArgumentNullException.ThrowIfNull(aonStageStatuses);
        return !aonDate.HasValue
            && !Contains(aonStageStatuses, StageStatus.Completed)
            && Contains(aonStageStatuses, StageStatus.Skipped);
    }

    public static bool HasSupplyOrderEvidence(DateOnly? supplyOrderDate, ProjectLifecycleStatus lifecycleStatus, string? currentStageCode)
    {
        if (supplyOrderDate.HasValue)
        {
            return true;
        }

        // Lower maturity numbers are further along; being *in* the Supply Order stage is not yet an order.
        var maturity = ProjectStageMaturityOrder.Resolve(lifecycleStatus, currentStageCode);
        return maturity != ProjectStageMaturityOrder.Unknown && maturity < ProjectStageMaturityOrder.SupplyOrder;
    }

    /// <summary>The band for a project's standing. <paramref name="pastAon"/> and <paramref name="supplyOrderPlaced"/> are null when not known.</summary>
    public static ArppProgressBucket Resolve(LifecycleAsOf lifecycle, bool inWork, bool? pastAon, bool? supplyOrderPlaced) => lifecycle switch
    {
        LifecycleAsOf.Completed => ArppProgressBucket.Completed,
        LifecycleAsOf.Cancelled => ArppProgressBucket.Cancelled,
        LifecycleAsOf.Active when inWork && supplyOrderPlaced == true => ArppProgressBucket.UnderDevelopment,
        LifecycleAsOf.Active when inWork && supplyOrderPlaced == false && pastAon == true => ArppProgressBucket.AonToSupplyOrder,
        LifecycleAsOf.Active when inWork && supplyOrderPlaced != true && pastAon == false => ArppProgressBucket.PreAon,
        _ => ArppProgressBucket.NotKnown
    };

    public static LifecycleAsOf LifecycleOf(ProjectLifecycleStatus status) => status switch
    {
        ProjectLifecycleStatus.Completed => LifecycleAsOf.Completed,
        ProjectLifecycleStatus.Cancelled => LifecycleAsOf.Cancelled,
        _ => LifecycleAsOf.Active
    };

    /// <summary>The completion month and year a progress list shows ("Mar 2026"), or the year alone.</summary>
    public static string? CompletionLabel(DateOnly? completedOn, int? completedYear, short? completedMonth) =>
        completedOn is { } on
            ? on.ToString("MMM yyyy", CultureInfo.InvariantCulture)
            : completedYear is { } year
                ? completedMonth is { } month and >= 1 and <= 12
                    ? new DateOnly(year, month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture)
                    : year.ToString(CultureInfo.InvariantCulture)
                : null;

    /// <summary>The date a completed project sorts by (latest first in a progress list).</summary>
    public static DateOnly? CompletionSort(DateOnly? completedOn, int? completedYear, short? completedMonth) =>
        completedOn ?? (completedYear is { } year ? new DateOnly(year, completedMonth is { } month and >= 1 and <= 12 ? month : 1, 1) : null);

    private static bool Contains(IReadOnlyCollection<StageStatus> statuses, StageStatus status)
    {
        foreach (var item in statuses)
        {
            if (item == status)
            {
                return true;
            }
        }

        return false;
    }
}
