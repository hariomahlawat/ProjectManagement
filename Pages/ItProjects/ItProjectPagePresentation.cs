using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.ItProjects;

/// <summary>
/// Wording for the project page (docs/ux/it-projects-redesign.md, PRs 6 and 7). The facts (status,
/// timing, year types) come from the read service; this only turns them into words.
/// </summary>
public static class ItProjectPagePresentation
{
    /// <summary>
    /// The one status line under the title. <paramref name="Tone"/> is calm, warn, late, nodate, ok
    /// or drop; <paramref name="Note"/> says what the figure is counted from.
    /// </summary>
    public sealed record HeroStatus(string Text, string? Note, string Tone);

    public static string LongDate(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Where the case is and for how long, in one sentence.</summary>
    public static HeroStatus Status(ItProject project, ItProjectStatus status, ItProjectTiming timing)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(timing);
        var stages = project.Stages.OrderBy(stage => stage.SortOrder).ToList();
        switch (status.Kind)
        {
            case ItProjectStatusKind.Completed:
            {
                var on = stages.FirstOrDefault(stage => stage.StageCode == ItProjectStages.Delivery)?.CompletedOn;
                return new HeroStatus(
                    on is { } date ? $"Delivered on {LongDate(date)}" : "Delivered",
                    timing.ElapsedDays is { } days ? $"{Days(days)} end to end" : null,
                    "ok");
            }

            case ItProjectStatusKind.Dropped:
                return new HeroStatus(
                    (status.CurrentStageLabel is { } at ? $"Dropped at {at}" : "Dropped") +
                        (project.DroppedOn is { } dropped ? $" on {LongDate(dropped)}" : string.Empty),
                    string.IsNullOrWhiteSpace(project.DroppedReason) ? null : project.DroppedReason,
                    "drop");

            case ItProjectStatusKind.NotStarted:
                return new HeroStatus(
                    timing.DaysAtStage is null or 0 ? "Not started, added today" : $"Not started, added {Days(timing.DaysAtStage.Value)} ago",
                    null,
                    "calm");
        }

        var stage = status.CurrentStageLabel ?? "the current stage";
        var previous = PreviousStageLabel(stages, status.CurrentStageCode) ?? "the previous stage";
        return timing.Basis switch
        {
            ItProjectTimeBasis.StageDate => new HeroStatus(
                $"At {stage} for {Days(timing.DaysAtStage ?? 0)}",
                timing.Since is { } since ? $"since {previous} closed on {LongDate(since)}" : null,
                timing.Band switch
                {
                    ItProjectAgeBand.Red => "late",
                    ItProjectAgeBand.Amber => "warn",
                    _ => "calm"
                }),
            ItProjectTimeBasis.RecordedMove => new HeroStatus(
                timing.Since is { } moved ? $"At {stage} since {LongDate(moved)}" : $"At {stage}",
                $"when the move was recorded; {previous} has no date",
                "nodate"),
            _ => new HeroStatus($"At {stage}", $"{previous} has no date, so the days are not counted", "nodate")
        };
    }

    /// <summary>
    /// One stop of the Journey. <paramref name="State"/> is done, nodate, cur, pend, skip or stop;
    /// <paramref name="Gap"/> is the days since the previous stage closed (or "so far" at the
    /// current stage), shown above the line; <paramref name="Travelled"/> draws the line into the
    /// stop as covered.
    /// </summary>
    public sealed record JourneyStep(
        string StageCode,
        string Label,
        string State,
        string Tone,
        string DateText,
        string? Gap,
        string? Cost,
        bool Travelled);

    /// <summary>The nine stops with their dates and the days between them, from the read service's timing.</summary>
    public static IReadOnlyList<JourneyStep> Journey(ItProject project, ItProjectStatus status, ItProjectTiming timing)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(timing);
        var stages = project.Stages.OrderBy(stage => stage.SortOrder).ToList();
        var gaps = timing.Gaps.ToDictionary(gap => gap.StageCode, gap => gap.Days, StringComparer.OrdinalIgnoreCase);
        var live = status.Kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress;

        // A stop is reached when it is done or current; a skipped stop is passed through when a
        // later stop is reached, so the covered line runs on across it.
        var reached = new bool[stages.Count];
        for (var index = stages.Count - 1; index >= 0; index--)
        {
            var stage = stages[index];
            var later = index + 1 < stages.Count && reached[index + 1];
            var isCurrent = live && stage.StageCode == status.CurrentStageCode;
            reached[index] = stage.IsSkipped ? later : stage.IsCompleted || isCurrent;
        }

        return stages.Select((stage, index) =>
        {
            var label = ItProjectStages.LabelOf(stage.StageCode);
            var cost = stage.CostLakhs is { } lakhs && !stage.IsSkipped ? ItProjectBoardPresentation.Cost(lakhs) : null;
            if (stage.IsSkipped)
            {
                return new JourneyStep(stage.StageCode, label, "skip", "calm", "skipped", null, null, index > 0 && reached[index]);
            }

            if (stage.IsCompleted)
            {
                var gap = gaps.TryGetValue(stage.StageCode, out var days) && days is { } d ? DaysShort(d) : null;
                return new JourneyStep(
                    stage.StageCode,
                    label,
                    stage.CompletedOn.HasValue ? "done" : "nodate",
                    "calm",
                    stage.CompletedOn is { } on ? ItProjectBoardPresentation.ShortDate(on) : "date not recorded",
                    gap,
                    cost,
                    index > 0 && reached[index]);
            }

            if (live && stage.StageCode == status.CurrentStageCode)
            {
                var running = timing.Basis == ItProjectTimeBasis.StageDate && timing.DaysAtStage is { } days
                    ? $"{DaysShort(days)} so far"
                    : null;
                var tone = timing.Basis != ItProjectTimeBasis.StageDate ? "calm" : timing.Band switch
                {
                    ItProjectAgeBand.Red => "late",
                    ItProjectAgeBand.Amber => "warn",
                    _ => "calm"
                };
                return new JourneyStep(stage.StageCode, label, "cur", tone, "now", running, cost, index > 0 && reached[index]);
            }

            if (status.Kind == ItProjectStatusKind.Dropped && stage.StageCode == status.CurrentStageCode)
            {
                return new JourneyStep(stage.StageCode, label, "stop", "calm", "dropped here", null, cost, false);
            }

            return new JourneyStep(stage.StageCode, label, "pend", "calm", "—", null, cost, false);
        }).ToList();
    }

    private static string DaysShort(int days) => days == 1 ? "1 d" : $"{days} d";

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
