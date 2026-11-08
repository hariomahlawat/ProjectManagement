using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.ItProjects;

/// <summary>
/// Wording for the project page hero (docs/ux/it-projects-redesign.md, PR 6). The facts (status,
/// timing, year types) come from the read service; this only turns them into the "now strip".
/// </summary>
public static class ItProjectPagePresentation
{
    /// <summary>One cell of the now strip. <paramref name="Tone"/> is calm, warn, late, nodate, ok or drop.</summary>
    public sealed record NowStripCell(string Key, string Value, string? Note, string Tone);

    public static string LongDate(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// The five cells under the title: where the case is and for how long, the supply order, when it
    /// started, how long it has run, and its type and IT PPP number in the latest year it counts in.
    /// </summary>
    public static IReadOnlyList<NowStripCell> NowStrip(
        ItProject project,
        ItProjectStatus status,
        ItProjectTiming timing,
        IReadOnlyList<ItProjectYearTypeInfo> yearTypes)
    {
        ArgumentNullException.ThrowIfNull(project);
        var stages = project.Stages.OrderBy(stage => stage.SortOrder).ToList();
        return new[]
        {
            Where(project, status, timing, stages),
            SupplyOrder(stages),
            Started(stages, timing),
            Elapsed(status, timing),
            Year(yearTypes)
        };
    }

    private static NowStripCell Where(ItProject project, ItProjectStatus status, ItProjectTiming timing, IReadOnlyList<ItProjectStage> stages)
    {
        switch (status.Kind)
        {
            case ItProjectStatusKind.Completed:
            {
                var on = stages.FirstOrDefault(stage => stage.StageCode == ItProjectStages.Delivery)?.CompletedOn;
                return new NowStripCell(
                    "Delivered",
                    on is { } date ? LongDate(date) : "date not recorded",
                    timing.ElapsedDays is { } days ? $"{Days(days)} end to end" : null,
                    "ok");
            }

            case ItProjectStatusKind.Dropped:
                return new NowStripCell(
                    status.CurrentStageLabel is { } at ? $"Dropped at {at}" : "Dropped",
                    project.DroppedOn is { } dropped ? LongDate(dropped) : "date not recorded",
                    string.IsNullOrWhiteSpace(project.DroppedReason) ? null : project.DroppedReason,
                    "drop");

            case ItProjectStatusKind.NotStarted:
                return new NowStripCell(
                    "Not started",
                    timing.DaysAtStage is null or 0 ? "added today" : Days(timing.DaysAtStage.Value),
                    timing.Since is { } added ? $"since it was added on {LongDate(added)}" : null,
                    "calm");
        }

        var stage = status.CurrentStageLabel ?? "the current stage";
        var previous = PreviousStageLabel(stages, status.CurrentStageCode);
        return timing.Basis switch
        {
            ItProjectTimeBasis.StageDate => new NowStripCell(
                $"At {stage} for",
                Days(timing.DaysAtStage ?? 0),
                timing.Since is { } since ? $"since {previous ?? "the previous stage"} closed on {LongDate(since)}" : null,
                timing.Band switch
                {
                    ItProjectAgeBand.Red => "late",
                    ItProjectAgeBand.Amber => "warn",
                    _ => "calm"
                }),
            ItProjectTimeBasis.RecordedMove => new NowStripCell(
                $"At {stage}",
                timing.Since is { } moved ? $"since {LongDate(moved)}" : "date not recorded",
                $"when the move was recorded; {previous ?? "the previous stage"} has no date",
                "nodate"),
            _ => new NowStripCell(
                $"At {stage}",
                "date not recorded",
                $"{previous ?? "the previous stage"} has no date",
                "nodate")
        };
    }

    private static NowStripCell SupplyOrder(IReadOnlyList<ItProjectStage> stages)
    {
        var so = stages.FirstOrDefault(stage => stage.StageCode == ItProjectStages.So);
        if (so is null)
        {
            return new NowStripCell("Supply order", "—", null, "calm");
        }

        var value = so.CostLakhs is { } cost ? ItProjectBoardPresentation.Cost(cost) : "—";
        var note = so.IsCompleted
            ? so.CompletedOn is { } placed ? $"placed {LongDate(placed)}" : "placed, date not recorded"
            : so.CostLakhs.HasValue ? "value entered, not placed yet" : "not placed yet";
        return new NowStripCell("Supply order", value, note, "calm");
    }

    private static NowStripCell Started(IReadOnlyList<ItProjectStage> stages, ItProjectTiming timing)
    {
        if (timing.StartedOn is not { } started)
        {
            return new NowStripCell("Started", "—", "no stage dated yet", "calm");
        }

        var first = stages.FirstOrDefault(stage => !stage.IsSkipped && stage.IsCompleted && stage.CompletedOn == started);
        return new NowStripCell("Started", LongDate(started), first is null ? null : $"{ItProjectStages.LabelOf(first.StageCode)} closed", "calm");
    }

    private static NowStripCell Elapsed(ItProjectStatus status, ItProjectTiming timing)
    {
        if (timing.ElapsedDays is not { } days)
        {
            return new NowStripCell("Elapsed", "—", "needs a dated stage", "calm");
        }

        var months = (int)Math.Round(days / 30.44, MidpointRounding.AwayFromZero);
        var span = months <= 1 ? "about a month" : $"about {months} months";
        var note = status.Kind switch
        {
            ItProjectStatusKind.Completed => $"{span}, to delivery",
            ItProjectStatusKind.Dropped => $"{span}, until it was dropped",
            _ => $"{span} so far"
        };
        return new NowStripCell("Elapsed", Days(days), note, "calm");
    }

    private static NowStripCell Year(IReadOnlyList<ItProjectYearTypeInfo> yearTypes)
    {
        var latest = yearTypes.LastOrDefault();
        if (latest is null)
        {
            return new NowStripCell("IT PPP", "—", null, "calm");
        }

        var how = latest.IsManual ? ", set by hand" : null;
        return new NowStripCell(
            $"FY {FinancialYearHelper.Format(latest.FinancialYearStart)} IT PPP",
            latest.Code,
            (latest.PppReferenceNumber ?? "number not entered") + how,
            "type-" + latest.Code.ToLowerInvariant());
    }

    /// <summary>The label of the last non-skipped stage before the current one.</summary>
    private static string? PreviousStageLabel(IReadOnlyList<ItProjectStage> stages, string? currentCode)
    {
        if (currentCode is null)
        {
            return null;
        }

        var currentOrder = ItProjectStages.OrderOf(currentCode);
        var previous = stages.LastOrDefault(stage => !stage.IsSkipped && stage.SortOrder < currentOrder);
        return previous is null ? null : ItProjectStages.LabelOf(previous.StageCode);
    }

    private static string Days(int days) => days == 1 ? "1 day" : $"{days.ToString("#,##0", CultureInfo.InvariantCulture)} days";
}
