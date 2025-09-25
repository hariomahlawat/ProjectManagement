using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Plans;

public sealed record PlanCalculatorManualOverride(DateOnly? Start, DateOnly? Due);

public sealed record PlanCalculatorOptions(
    string AnchorStageCode,
    DateOnly AnchorDate,
    bool SkipWeekends,
    PlanTransitionRule TransitionRule,
    bool PncApplicable,
    IReadOnlyDictionary<string, int> DurationsDays,
    IReadOnlyDictionary<string, PlanCalculatorManualOverride>? ManualOverrides);

public sealed class PlanCalculator
{
    private readonly IReadOnlyList<StageTemplate> _orderedStages;
    private readonly ILookup<string, string> _dependencies;
    private readonly Dictionary<string, StageTemplate> _stageByCode;

    public PlanCalculator(IEnumerable<StageTemplate> stages, IEnumerable<StageDependencyTemplate> dependencies)
    {
        if (stages is null)
        {
            throw new ArgumentNullException(nameof(stages));
        }

        if (dependencies is null)
        {
            throw new ArgumentNullException(nameof(dependencies));
        }

        _orderedStages = stages
            .OrderBy(s => s.Sequence)
            .ToList();

        _stageByCode = _orderedStages
            .ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

        _dependencies = dependencies
            .ToLookup(d => d.FromStageCode, d => d.DependsOnStageCode, StringComparer.OrdinalIgnoreCase);
    }

    public IDictionary<string, (DateOnly start, DateOnly due)> Compute(PlanCalculatorOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!_stageByCode.TryGetValue(options.AnchorStageCode, out var anchorStage))
        {
            throw new InvalidOperationException($"Anchor stage '{options.AnchorStageCode}' was not found in the template set.");
        }

        if (options.DurationsDays is null)
        {
            throw new ArgumentNullException(nameof(options.DurationsDays));
        }

        var includedStages = new HashSet<string>(_stageByCode.Keys, StringComparer.OrdinalIgnoreCase);
        if (!options.PncApplicable)
        {
            includedStages.Remove("PNC");
        }

        var manualOverrides = options.ManualOverrides ?? new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, (DateOnly start, DateOnly due)>(StringComparer.OrdinalIgnoreCase);

        foreach (var stage in _orderedStages)
        {
            if (stage.Sequence < anchorStage.Sequence)
            {
                continue;
            }

            if (!includedStages.Contains(stage.Code))
            {
                continue;
            }

            var duration = options.DurationsDays.TryGetValue(stage.Code, out var length)
                ? Math.Max(length, 0)
                : 0;

            var hasManualOverride = manualOverrides.TryGetValue(stage.Code, out var manualOverride);

            DateOnly start;

            if (string.Equals(stage.Code, options.AnchorStageCode, StringComparison.OrdinalIgnoreCase))
            {
                start = NormalizeDate(manualOverride?.Start ?? options.AnchorDate, options.SkipWeekends);
            }
            else
            {
                var earliest = CalculateEarliestStart(stage.Code, results, includedStages, options);
                if (!earliest.HasValue)
                {
                    earliest = NormalizeDate(options.AnchorDate, options.SkipWeekends);
                }

                if (hasManualOverride && manualOverride?.Start is DateOnly manualStart)
                {
                    var normalizedManual = NormalizeDate(manualStart, options.SkipWeekends);
                    start = normalizedManual < earliest.Value ? earliest.Value : normalizedManual;
                }
                else
                {
                    start = earliest.Value;
                }
            }

            var due = duration <= 0
                ? start
                : CalculateDue(start, duration, options.SkipWeekends);

            if (hasManualOverride && manualOverride?.Due is DateOnly manualDue)
            {
                var normalizedManualDue = NormalizeDate(manualDue, options.SkipWeekends);
                due = normalizedManualDue < start ? start : normalizedManualDue;
            }

            results[stage.Code] = (start, due);
        }

        return results;
    }

    private DateOnly? CalculateEarliestStart(
        string stageCode,
        IReadOnlyDictionary<string, (DateOnly start, DateOnly due)> computed,
        HashSet<string> includedStages,
        PlanCalculatorOptions options)
    {
        DateOnly? earliest = null;

        if (!_dependencies.Contains(stageCode))
        {
            return earliest;
        }

        foreach (var dependencyCode in _dependencies[stageCode])
        {
            if (!includedStages.Contains(dependencyCode))
            {
                continue;
            }

            if (!computed.TryGetValue(dependencyCode, out var dependencyDates))
            {
                continue;
            }

            var candidate = dependencyDates.due;

            if (options.TransitionRule == PlanTransitionRule.NextWorkingDay)
            {
                candidate = candidate.AddDays(1);
            }

            candidate = NormalizeDate(candidate, options.SkipWeekends);

            if (!earliest.HasValue || candidate > earliest.Value)
            {
                earliest = candidate;
            }
        }

        return earliest;
    }

    private static DateOnly CalculateDue(DateOnly start, int durationDays, bool skipWeekends)
    {
        if (durationDays <= 0)
        {
            return start;
        }

        var due = start;
        var remaining = durationDays - 1;

        while (remaining > 0)
        {
            due = due.AddDays(1);
            if (skipWeekends)
            {
                while (IsWeekend(due))
                {
                    due = due.AddDays(1);
                }
            }

            remaining--;
        }

        return NormalizeDate(due, skipWeekends);
    }

    private static DateOnly NormalizeDate(DateOnly date, bool skipWeekends)
    {
        if (!skipWeekends)
        {
            return date;
        }

        return NextWorkday(date);
    }

    private static DateOnly NextWorkday(DateOnly date)
    {
        var current = date;
        while (IsWeekend(current))
        {
            current = current.AddDays(1);
        }

        return current;
    }

    private static bool IsWeekend(DateOnly date)
    {
        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }
}
