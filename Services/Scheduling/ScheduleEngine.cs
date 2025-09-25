using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Scheduling;

public class ScheduleEngine : IScheduleEngine
{
    public IDictionary<string, (DateOnly start, DateOnly due)> ComputeForecast(
        IReadOnlyList<StageTemplate> templates,
        IReadOnlyList<StageDependencyTemplate> deps,
        IReadOnlyDictionary<string, int> durationsDays,
        IReadOnlyDictionary<string, ProjectStage> execution,
        ScheduleOptions opts)
    {
        if (templates is null)
        {
            throw new ArgumentNullException(nameof(templates));
        }

        if (deps is null)
        {
            throw new ArgumentNullException(nameof(deps));
        }

        if (durationsDays is null)
        {
            throw new ArgumentNullException(nameof(durationsDays));
        }

        if (execution is null)
        {
            throw new ArgumentNullException(nameof(execution));
        }

        // Filter stages based on PNC applicability and prepare lookup for quick membership tests.
        var nodes = templates
            .Where(t => opts.PncApplicable || !string.Equals(t.Code, "PNC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Sequence)
            .ToList();

        var nodeSet = new HashSet<string>(nodes.Select(n => n.Code), StringComparer.OrdinalIgnoreCase);

        // Build predecessor lookup respecting the filtered nodes.
        var relevantDeps = deps
            .Where(d => nodeSet.Contains(d.FromStageCode) && nodeSet.Contains(d.DependsOnStageCode))
            .ToLookup(d => d.FromStageCode, d => d.DependsOnStageCode, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, (DateOnly start, DateOnly due)>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            if (!execution.TryGetValue(node.Code, out var stage))
            {
                throw new InvalidOperationException($"Execution stage '{node.Code}' is missing.");
            }

            DateOnly? ready = null;
            foreach (var predecessor in relevantDeps[node.Code])
            {
                if (!execution.TryGetValue(predecessor, out var predecessorStage))
                {
                    throw new InvalidOperationException($"Execution stage '{predecessor}' is missing.");
                }

                if (predecessorStage.Status == StageStatus.Skipped)
                {
                    continue;
                }

                var predecessorFinish = predecessorStage.CompletedOn
                    ?? (result.TryGetValue(predecessor, out var computed) ? computed.due
                        : predecessorStage.ForecastDue ?? predecessorStage.PlannedDue ?? predecessorStage.PlannedStart
                        ?? opts.Today);

                var nextReady = AddGap(predecessorFinish, opts);
                ready = ready is null ? nextReady : Max(ready.Value, nextReady);
            }

            var start = stage.ActualStart
                ?? ready
                ?? stage.PlannedStart
                ?? opts.Today;

            if (!durationsDays.TryGetValue(node.Code, out var duration))
            {
                throw new InvalidOperationException($"DurationDays missing for stage '{node.Code}'.");
            }

            duration = Math.Max(1, duration);
            var due = stage.CompletedOn ?? AddWorkingDays(start, duration - 1, opts);

            result[node.Code] = (start, due);
        }

        return result;
    }

    private static DateOnly AddGap(DateOnly date, ScheduleOptions opts)
    {
        var adjusted = opts.TransitionRule == PlanTransitionRule.NextWorkingDay ? date.AddDays(1) : date;
        return BumpWeekend(adjusted, opts.SkipWeekends);
    }

    private static DateOnly AddWorkingDays(DateOnly start, int additionalDays, ScheduleOptions opts)
    {
        var current = start;
        for (var i = 0; i < additionalDays; i++)
        {
            current = current.AddDays(1);
            current = BumpWeekend(current, opts.SkipWeekends);
        }

        return current;
    }

    private static DateOnly BumpWeekend(DateOnly date, bool skipWeekends)
    {
        if (!skipWeekends)
        {
            return date;
        }

        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => date.AddDays(2),
            DayOfWeek.Sunday => date.AddDays(1),
            _ => date
        };
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;
}
