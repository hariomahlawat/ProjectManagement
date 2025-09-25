using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Plans;

public sealed class StageFlowService
{
    private readonly IReadOnlyDictionary<string, string[]> _predecessors;
    private readonly IReadOnlyDictionary<string, string[]> _successors;
    private readonly bool _skipWeekends;
    private readonly bool _nextWorkingDay;

    public StageFlowService(IEnumerable<StageDependencyTemplate> dependencies, bool skipWeekends, PlanTransitionRule transitionRule)
    {
        if (dependencies is null)
        {
            throw new ArgumentNullException(nameof(dependencies));
        }

        _skipWeekends = skipWeekends;
        _nextWorkingDay = transitionRule == PlanTransitionRule.NextWorkingDay;

        var predecessorLookup = dependencies
            .GroupBy(d => d.FromStageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(d => d.DependsOnStageCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        _predecessors = predecessorLookup;

        var successorMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.DependsOnStageCode) || string.IsNullOrWhiteSpace(dependency.FromStageCode))
            {
                continue;
            }

            if (!successorMap.TryGetValue(dependency.DependsOnStageCode, out var list))
            {
                list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                successorMap[dependency.DependsOnStageCode] = list;
            }

            list.Add(dependency.FromStageCode);
        }

        _successors = successorMap.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetPredecessors(string stageCode)
    {
        if (stageCode == null)
        {
            throw new ArgumentNullException(nameof(stageCode));
        }

        return _predecessors.TryGetValue(stageCode, out var preds) ? preds : Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSuccessors(string stageCode)
    {
        if (stageCode == null)
        {
            throw new ArgumentNullException(nameof(stageCode));
        }

        return _successors.TryGetValue(stageCode, out var list) ? list : Array.Empty<string>();
    }

    public DateOnly Bump(DateOnly date)
    {
        if (!_skipWeekends)
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

    public DateOnly GapAfter(DateOnly date)
    {
        var next = _nextWorkingDay ? date.AddDays(1) : date;
        return Bump(next);
    }

    public DateOnly? ComputeAutoStart(
        string stageCode,
        IReadOnlyDictionary<string, DateOnly?> completedOn,
        IReadOnlySet<string>? skipped)
    {
        if (stageCode == null)
        {
            throw new ArgumentNullException(nameof(stageCode));
        }

        if (completedOn == null)
        {
            throw new ArgumentNullException(nameof(completedOn));
        }

        skipped ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!_predecessors.TryGetValue(stageCode, out var predecessors) || predecessors.Length == 0)
        {
            return null;
        }

        var edges = new List<DateOnly>();
        foreach (var predecessor in predecessors)
        {
            if (skipped.Contains(predecessor))
            {
                continue;
            }

            if (!completedOn.TryGetValue(predecessor, out var completed) || completed is null)
            {
                return null;
            }

            edges.Add(GapAfter(completed.Value));
        }

        return edges.Count == 0 ? null : edges.Max();
    }
}
