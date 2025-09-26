using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

public sealed class TrackerBuilder
{
    private readonly IReadOnlyList<StageTemplate> _templates;
    private readonly ILookup<string, string> _predecessors;

    public TrackerBuilder(IEnumerable<StageTemplate> templates, IEnumerable<StageDependencyTemplate> dependencies)
    {
        _templates = templates
            .OrderBy(t => t.Sequence)
            .ToList();

        _predecessors = dependencies
            .ToLookup(d => d.FromStageCode, d => d.DependsOnStageCode, StringComparer.OrdinalIgnoreCase);
    }

    public TrackerVm Build(IEnumerable<ProjectStage> stages, bool pncApplicable, DateOnly today)
    {
        var stageMap = stages.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);
        var ordered = _templates
            .Where(t => pncApplicable || !IsPnc(t.Code))
            .ToList();

        var current = DetermineCurrent(ordered, stageMap);
        var vm = new TrackerVm
        {
            CurrentCode = current ?? string.Empty,
            PncApplicable = pncApplicable
        };

        foreach (var template in _templates)
        {
            if (!pncApplicable && IsPnc(template.Code))
            {
                // Skip visible rendering later but keep placeholder for branch join logic.
            }

            stageMap.TryGetValue(template.Code, out var stage);
            var node = BuildNode(template, stage, current, today, pncApplicable);

            if (string.Equals(template.Code, "TEC", StringComparison.OrdinalIgnoreCase))
            {
                vm.BranchTop.Add(node);
            }
            else if (string.Equals(template.Code, "BM", StringComparison.OrdinalIgnoreCase))
            {
                vm.BranchBottom.Add(node);
            }
            else
            {
                vm.Main.Add(node);
            }
        }

        AddEdges(vm, stageMap, today, pncApplicable, vm.CurrentCode);
        return vm;
    }

    private string? DetermineCurrent(IEnumerable<StageTemplate> ordered, IDictionary<string, ProjectStage> stageMap)
    {
        var allDone = true;
        foreach (var template in ordered)
        {
            if (!stageMap.TryGetValue(template.Code, out var stage))
            {
                continue;
            }

            if (stage.Status is StageStatus.Completed or StageStatus.Skipped)
            {
                continue;
            }

            allDone = false;

            var predecessors = _predecessors[template.Code];
            var ready = true;
            foreach (var pred in predecessors)
            {
                if (!stageMap.TryGetValue(pred, out var predStage))
                {
                    continue;
                }

                if (predStage.Status is not StageStatus.Completed and not StageStatus.Skipped)
                {
                    ready = false;
                    break;
                }
            }

            if (ready)
            {
                return template.Code;
            }
        }

        return allDone ? null : ordered.LastOrDefault()?.Code;
    }

    private static TrackNodeVm BuildNode(StageTemplate template, ProjectStage? stage, string? currentCode, DateOnly today, bool pncApplicable)
    {
        var state = TrackNodeState.Todo;
        if (stage != null)
        {
            if (stage.Status is StageStatus.Completed or StageStatus.Skipped)
            {
                state = TrackNodeState.Done;
            }
            else if (string.Equals(stage.StageCode, currentCode, StringComparison.OrdinalIgnoreCase))
            {
                state = TrackNodeState.Current;
            }
        }

        var slip = CalculateSlip(stage, today);
        var tooltip = BuildTooltip(stage);

        return new TrackNodeVm
        {
            Code = template.Code,
            Name = template.Name,
            State = state,
            PlannedStart = stage?.PlannedStart,
            PlannedDue = stage?.PlannedDue,
            ActualStart = stage?.ActualStart,
            CompletedOn = stage?.CompletedOn,
            SlipDays = slip,
            Tooltip = tooltip,
            IsOptional = template.Optional,
            IsVisible = pncApplicable || !IsPnc(template.Code)
        };
    }

    private static void AddEdges(
        TrackerVm vm,
        IDictionary<string, ProjectStage> stages,
        DateOnly today,
        bool pncApplicable,
        string currentCode)
    {
        string? previousVisible = null;
        foreach (var node in vm.Main)
        {
            if (!node.IsVisible)
            {
                continue;
            }

            if (previousVisible != null)
            {
                vm.Edges.Add(BuildEdge(stages, previousVisible, node.Code, today, currentCode));
            }

            previousVisible = node.Code;
        }

        if (vm.BranchTop.Count == 0 || vm.BranchBottom.Count == 0)
        {
            return;
        }

        vm.Edges.Add(BuildEdge(stages, "BID", vm.BranchTop[0].Code, today, currentCode));
        vm.Edges.Add(BuildEdge(stages, "BID", vm.BranchBottom[0].Code, today, currentCode));
        vm.Edges.Add(BuildEdge(stages, vm.BranchTop[0].Code, "COB", today, currentCode));
        vm.Edges.Add(BuildEdge(stages, vm.BranchBottom[0].Code, "COB", today, currentCode));
    }

    private static TrackEdgeVm BuildEdge(
        IDictionary<string, ProjectStage> stages,
        string fromCode,
        string toCode,
        DateOnly today,
        string currentCode)
    {
        stages.TryGetValue(fromCode, out var from);
        stages.TryGetValue(toCode, out var to);

        var label = string.Empty;
        var variant = "neutral";

        if (from != null && from.Status == StageStatus.Completed && from.CompletedOn.HasValue && from.ActualStart.HasValue)
        {
            var days = Math.Max(1, DaysBetween(from.ActualStart.Value, from.CompletedOn.Value));
            if (from.PlannedDue.HasValue)
            {
                var slip = DaysBetween(from.PlannedDue.Value, from.CompletedOn.Value);
                if (slip <= 0)
                {
                    label = $"{days}d on time";
                    variant = "good";
                }
                else
                {
                    label = $"{days}d late by {slip}d";
                    variant = "bad";
                }
            }
            else
            {
                label = $"{days}d completed";
            }
        }
        else if (to != null && (to.Status == StageStatus.InProgress || string.Equals(to.StageCode, currentCode, StringComparison.OrdinalIgnoreCase)))
        {
            if (to.ActualStart.HasValue)
            {
                var duration = Math.Max(1, DaysBetween(to.ActualStart.Value, today));
                label = $"in progress {duration}d";
                variant = "progress";
            }
        }

        return new TrackEdgeVm
        {
            From = fromCode,
            To = toCode,
            Label = label,
            Variant = variant
        };
    }

    private static int CalculateSlip(ProjectStage? stage, DateOnly today)
    {
        if (stage == null)
        {
            return 0;
        }

        if (stage.CompletedOn.HasValue && stage.PlannedDue.HasValue)
        {
            return Math.Max(0, DaysBetween(stage.PlannedDue.Value, stage.CompletedOn.Value));
        }

        if (stage.PlannedDue.HasValue)
        {
            return Math.Max(0, DaysBetween(stage.PlannedDue.Value, today));
        }

        return 0;
    }

    private static string BuildTooltip(ProjectStage? stage)
    {
        if (stage == null)
        {
            return string.Empty;
        }

        var plannedStart = stage.PlannedStart?.ToString("dd MMM yyyy") ?? "—";
        var plannedDue = stage.PlannedDue?.ToString("dd MMM yyyy") ?? "—";
        var actualStart = stage.ActualStart?.ToString("dd MMM yyyy") ?? "—";
        var completed = stage.CompletedOn?.ToString("dd MMM yyyy") ?? "—";
        return $"Planned: {plannedStart} – {plannedDue} • Actual: {actualStart} – {completed}";
    }

    private static bool IsPnc(string code)
        => string.Equals(code, "PNC", StringComparison.OrdinalIgnoreCase);

    private static int DaysBetween(DateOnly start, DateOnly end)
        => (end.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days;
}
