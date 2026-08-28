using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Resolves lifecycle chronology from the configured workflow dependency graph.
/// Display order is deliberately ignored for dependency decisions: a stage's
/// permissible start boundary is determined only by its required predecessors.
///
/// For converging branches, the latest effective predecessor completion controls
/// the boundary. A skipped dependency contributes no date of its own; its actual
/// dependency ancestry is traversed until a dated completed stage is reached.
/// Same-day commencement remains permissible and the following day remains the
/// conventional suggested start date.
/// </summary>
public static class StageDateSuggestionResolver
{
    public static StageDateSuggestion Resolve(
        ProjectStageWorkflowSnapshot workflow,
        IEnumerable<ProjectStage> projectStages,
        string? stageCode)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(projectStages);

        var stageLookup = projectStages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return Resolve(
            workflow,
            stageCode,
            code => stageLookup.TryGetValue(code, out var stage)
                ? new StageChronologyState(stage.Status, stage.CompletedOn)
                : null);
    }

    /// <summary>
    /// Resolves chronology against an effective stage-state projection. This is
    /// used by request and approval workflows so the same graph semantics apply
    /// to approved actuals and pending/proposed lifecycle states.
    /// </summary>
    public static StageDateSuggestion Resolve(
        ProjectStageWorkflowSnapshot workflow,
        string? stageCode,
        Func<string, StageChronologyState?> stateAccessor)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(stateAccessor);

        if (string.IsNullOrWhiteSpace(stageCode) || !workflow.ContainsStage(stageCode))
        {
            return StageDateSuggestion.None;
        }

        var requiredPredecessors = workflow.RequiredPredecessors(stageCode);
        if (requiredPredecessors.Count == 0)
        {
            return StageDateSuggestion.None;
        }

        var stageNameLookup = workflow.Stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.Code))
            .GroupBy(stage => stage.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

        var branchResults = requiredPredecessors
            .Select(predecessorCode => ResolveDependencyBranch(
                workflow,
                predecessorCode,
                stateAccessor,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        // If any mandatory branch has no trustworthy completion boundary yet,
        // do not manufacture a permissive boundary from the remaining branches.
        // Predecessor validation will separately decide whether that unresolved
        // branch blocks the requested transition.
        var unresolved = branchResults
            .Where(result => !result.IsResolved)
            .OrderBy(result => workflow.OrderOf(result.SourceStageCode))
            .ThenBy(result => result.SourceStageCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (unresolved is not null)
        {
            return new StageDateSuggestion(
                SuggestedStartDate: null,
                EarliestAllowedStartDate: null,
                SourceStageCode: unresolved.SourceStageCode,
                SourceStageName: ResolveStageName(stageNameLookup, unresolved.SourceStageCode),
                SourceCompletionDate: unresolved.CompletionDate,
                SkippedStageCount: unresolved.SkippedStageCount);
        }

        var controlling = branchResults
            .Where(result => result.CompletionDate.HasValue)
            .OrderByDescending(result => result.CompletionDate!.Value.DayNumber)
            .ThenByDescending(result => workflow.OrderOf(result.SourceStageCode))
            .ThenBy(result => result.SourceStageCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (controlling is null || !controlling.CompletionDate.HasValue)
        {
            return StageDateSuggestion.None;
        }

        var completionDate = controlling.CompletionDate.Value;
        return new StageDateSuggestion(
            SuggestedStartDate: completionDate.AddDays(1),
            EarliestAllowedStartDate: completionDate,
            SourceStageCode: controlling.SourceStageCode,
            SourceStageName: ResolveStageName(stageNameLookup, controlling.SourceStageCode),
            SourceCompletionDate: completionDate,
            SkippedStageCount: controlling.SkippedStageCount);
    }

    private static BranchResolution ResolveDependencyBranch(
        ProjectStageWorkflowSnapshot workflow,
        string dependencyCode,
        Func<string, StageChronologyState?> stateAccessor,
        ISet<string> path)
    {
        if (!path.Add(dependencyCode))
        {
            // A workflow dependency cycle is invalid configuration. Fail closed
            // for chronology rather than deriving a boundary from a partial path.
            return BranchResolution.Unresolved(dependencyCode);
        }

        try
        {
            var state = stateAccessor(dependencyCode);
            if (state is null)
            {
                return BranchResolution.Unresolved(dependencyCode);
            }

            if (state.Status == StageStatus.Skipped)
            {
                var ancestors = workflow.RequiredPredecessors(dependencyCode);
                if (ancestors.Count == 0)
                {
                    return BranchResolution.ResolvedWithoutDate(skippedStageCount: 1);
                }

                var ancestorResults = ancestors
                    .Select(ancestorCode => ResolveDependencyBranch(
                        workflow,
                        ancestorCode,
                        stateAccessor,
                        new HashSet<string>(path, StringComparer.OrdinalIgnoreCase)))
                    .ToArray();

                var unresolvedAncestor = ancestorResults
                    .Where(result => !result.IsResolved)
                    .OrderBy(result => workflow.OrderOf(result.SourceStageCode))
                    .ThenBy(result => result.SourceStageCode, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (unresolvedAncestor is not null)
                {
                    return unresolvedAncestor with
                    {
                        SkippedStageCount = unresolvedAncestor.SkippedStageCount + 1
                    };
                }

                var controllingAncestor = ancestorResults
                    .Where(result => result.CompletionDate.HasValue)
                    .OrderByDescending(result => result.CompletionDate!.Value.DayNumber)
                    .ThenByDescending(result => workflow.OrderOf(result.SourceStageCode))
                    .ThenBy(result => result.SourceStageCode, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (controllingAncestor is null)
                {
                    return BranchResolution.ResolvedWithoutDate(
                        skippedStageCount: ancestorResults
                            .Select(result => result.SkippedStageCount)
                            .DefaultIfEmpty(0)
                            .Max() + 1);
                }

                return controllingAncestor with
                {
                    SkippedStageCount = controllingAncestor.SkippedStageCount + 1
                };
            }

            if (state.Status == StageStatus.Completed && state.CompletionDate.HasValue)
            {
                return BranchResolution.ResolvedWithDate(
                    dependencyCode,
                    state.CompletionDate.Value,
                    skippedStageCount: 0);
            }

            // A completed stage without a completion date cannot supply a reliable
            // chronology boundary. Likewise, any non-completed mandatory dependency
            // remains unresolved for chronology purposes.
            return BranchResolution.Unresolved(dependencyCode, state.CompletionDate);
        }
        finally
        {
            path.Remove(dependencyCode);
        }
    }

    private static string? ResolveStageName(
        IReadOnlyDictionary<string, string> stageNameLookup,
        string? stageCode)
        => !string.IsNullOrWhiteSpace(stageCode)
           && stageNameLookup.TryGetValue(stageCode, out var name)
            ? name
            : null;

    private sealed record BranchResolution(
        bool IsResolved,
        DateOnly? CompletionDate,
        string? SourceStageCode,
        int SkippedStageCount)
    {
        public static BranchResolution Unresolved(
            string sourceStageCode,
            DateOnly? completionDate = null)
            => new(false, completionDate, sourceStageCode, 0);

        public static BranchResolution ResolvedWithDate(
            string sourceStageCode,
            DateOnly completionDate,
            int skippedStageCount)
            => new(true, completionDate, sourceStageCode, skippedStageCount);

        public static BranchResolution ResolvedWithoutDate(int skippedStageCount)
            => new(true, null, null, skippedStageCount);
    }
}

/// <summary>
/// Effective state used for chronology resolution. Callers may project approved
/// project rows, pending PO requests or approval-time effective lifecycle state.
/// </summary>
public sealed record StageChronologyState(
    StageStatus Status,
    DateOnly? CompletionDate);

public sealed record StageDateSuggestion(
    DateOnly? SuggestedStartDate,
    DateOnly? EarliestAllowedStartDate,
    string? SourceStageCode,
    string? SourceStageName,
    DateOnly? SourceCompletionDate,
    int SkippedStageCount)
{
    public static StageDateSuggestion None { get; } = new(null, null, null, null, null, 0);

    public bool HasSuggestion => SuggestedStartDate.HasValue;

    public bool HasStartBoundary => EarliestAllowedStartDate.HasValue;
}
