using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>What the stage editor posts for one stage.</summary>
public sealed record ItProjectStageInput(
    string StageCode,
    bool IsCompleted,
    DateOnly? CompletedOn,
    bool IsSkipped,
    decimal? CostLakhs);

/// <summary>The normalised state of one stage after the rules have run.</summary>
public sealed record ItProjectStageState(
    string StageCode,
    int SortOrder,
    bool IsCompleted,
    DateOnly? CompletedOn,
    bool IsSkipped,
    decimal? CostLakhs);

public sealed record ItProjectStageRulesResult(
    IReadOnlyList<ItProjectStageState> Stages,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// The one place that knows the IT project stage rules (docs/ux/it-projects.md, "Stage rules").
/// Pure: it takes the posted nine stages and returns the final state or the errors.
///
/// 1. The highest completed non-skipped stage decides completion: every non-skipped stage up to
///    it is complete, every later stage is not. That is both "completing a later stage completes
///    the earlier ones" and "reopening a stage reopens the later ones" in one deterministic rule.
/// 2. A completion date implies completion (a dated stage counts as ticked).
/// 3. Dates are optional. Where present they must not go backwards in stage order.
/// 4. Only Benchmarking and PNC may be skipped. A skipped stage is neither complete nor dated.
/// 5. The IPA cost (on IT ARPPP / IPA) and the SO cost are optional, in lakhs, never negative, and
///    kept whether or not their stage is complete.
/// </summary>
public static class ItProjectStageRules
{
    public const decimal MaxCostLakhs = 99_999_999.99m;

    /// <summary>The same, with the messages in a module's words (the first stage is "MOLTI ARPPP / IPA" for MOLTI projects).</summary>
    public static ItProjectStageRulesResult Apply(IReadOnlyCollection<ItProjectStageInput> input, string module)
    {
        var result = Apply(input);
        return result.Errors.Count == 0 || module == ItProjectModules.It
            ? result
            : result with { Errors = result.Errors.Select(error => ItProjectModules.Reword(error, module)).ToList() };
    }

    public static ItProjectStageRulesResult Apply(IReadOnlyCollection<ItProjectStageInput> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new List<string>();

        var byCode = new Dictionary<string, ItProjectStageInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in input)
        {
            if (!ItProjectStages.IsKnown(item.StageCode))
            {
                errors.Add($"Unknown stage '{item.StageCode}'.");
                continue;
            }

            if (!byCode.TryAdd(item.StageCode, item))
            {
                errors.Add($"Stage {ItProjectStages.LabelOf(item.StageCode)} was posted twice.");
            }
        }

        foreach (var definition in ItProjectStages.All)
        {
            if (!byCode.ContainsKey(definition.Code))
            {
                errors.Add($"Stage {definition.Label} is missing.");
            }
        }

        if (errors.Count > 0)
        {
            return new ItProjectStageRulesResult(Array.Empty<ItProjectStageState>(), errors);
        }

        // Rule 4 and rule 5: validate the per-stage inputs before the chain is resolved.
        var working = new List<(ItProjectStages.Definition Definition, bool Completed, DateOnly? Date, bool Skipped, decimal? Cost)>();
        foreach (var definition in ItProjectStages.All)
        {
            var item = byCode[definition.Code];
            var skipped = item.IsSkipped;
            if (skipped && !definition.IsOptional)
            {
                errors.Add($"{definition.Label} cannot be skipped.");
                skipped = false;
            }

            decimal? cost = null;
            if (definition.CarriesCost)
            {
                cost = item.CostLakhs;
                if (cost is < 0)
                {
                    errors.Add($"The {definition.Label} cost cannot be negative.");
                }
                else if (cost > MaxCostLakhs)
                {
                    errors.Add($"The {definition.Label} cost is too large.");
                }
                else if (cost.HasValue)
                {
                    cost = decimal.Round(cost.Value, 2, MidpointRounding.AwayFromZero);
                }
            }
            else if (item.CostLakhs.HasValue)
            {
                errors.Add($"Only the IT ARPPP / IPA and SO stages carry a cost ({definition.Label}).");
            }

            // Rule 2: a date means done.
            var completed = !skipped && (item.IsCompleted || item.CompletedOn.HasValue);
            var date = skipped ? null : item.CompletedOn;
            working.Add((definition, completed, date, skipped, cost));
        }

        // Rule 1: the highest completed non-skipped stage decides the chain.
        var highest = working.Where(stage => !stage.Skipped && stage.Completed)
            .Select(stage => stage.Definition.SortOrder)
            .DefaultIfEmpty(0)
            .Max();

        var states = new List<ItProjectStageState>(working.Count);
        foreach (var stage in working)
        {
            var completed = !stage.Skipped && stage.Definition.SortOrder <= highest;
            states.Add(new ItProjectStageState(
                stage.Definition.Code,
                stage.Definition.SortOrder,
                completed,
                completed ? stage.Date : null,
                stage.Skipped,
                stage.Cost));
        }

        // Rule 3: dates never go backwards.
        ItProjectStageState? previousDated = null;
        foreach (var state in states.Where(state => state.CompletedOn.HasValue))
        {
            if (previousDated is not null && state.CompletedOn < previousDated.CompletedOn)
            {
                errors.Add(
                    $"{ItProjectStages.LabelOf(state.StageCode)} cannot be completed before {ItProjectStages.LabelOf(previousDated.StageCode)} " +
                    $"({previousDated.CompletedOn:dd MMM yyyy}).");
            }

            previousDated = state;
        }

        return new ItProjectStageRulesResult(states, errors);
    }

    /// <summary>
    /// The editor gesture "untick stage N": that stage and every later one become open with
    /// their dates cleared, so that <see cref="Apply"/> does not re-complete N from a later tick.
    /// Unknown codes leave the input unchanged.
    /// </summary>
    public static IReadOnlyList<ItProjectStageInput> Reopen(IReadOnlyCollection<ItProjectStageInput> input, string stageCode)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!ItProjectStages.IsKnown(stageCode))
        {
            return input.ToList();
        }

        var from = ItProjectStages.OrderOf(stageCode);
        return input
            .Select(item => ItProjectStages.IsKnown(item.StageCode) && ItProjectStages.OrderOf(item.StageCode) >= from
                ? item with { IsCompleted = false, CompletedOn = null }
                : item)
            .ToList();
    }

    /// <summary>
    /// The stages a save would auto-complete or reopen compared with the stored rows, for the
    /// confirm dialog and the audit entry. Both lists are in stage order.
    /// </summary>
    public static (IReadOnlyList<string> Completed, IReadOnlyList<string> Reopened) Diff(
        IEnumerable<ItProjectStage> current,
        IReadOnlyList<ItProjectStageState> next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);

        var before = current.ToDictionary(stage => stage.StageCode, stage => stage.IsCompleted, StringComparer.OrdinalIgnoreCase);
        var completed = new List<string>();
        var reopened = new List<string>();
        foreach (var state in next.OrderBy(state => state.SortOrder))
        {
            var was = before.TryGetValue(state.StageCode, out var value) && value;
            if (!was && state.IsCompleted)
            {
                completed.Add(state.StageCode);
            }
            else if (was && !state.IsCompleted)
            {
                reopened.Add(state.StageCode);
            }
        }

        return (completed, reopened);
    }
}
