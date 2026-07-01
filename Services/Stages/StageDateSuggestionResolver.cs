using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Resolves the suggested start date for a stage from the immediately preceding
/// stage in the project's configured workflow. Consecutive skipped stages are
/// traversed backwards until the first non-skipped predecessor is found.
/// </summary>
public static class StageDateSuggestionResolver
{
    public static StageDateSuggestion Resolve(
        ProjectStageWorkflowSnapshot workflow,
        IEnumerable<ProjectStage> projectStages,
        string? stageCode)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return Resolve(workflow.Stages, projectStages, stageCode);
    }

    public static StageDateSuggestion Resolve(
        IReadOnlyList<WorkflowStageDefinition> orderedStages,
        IEnumerable<ProjectStage> projectStages,
        string? stageCode)
    {
        ArgumentNullException.ThrowIfNull(orderedStages);
        ArgumentNullException.ThrowIfNull(projectStages);

        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return StageDateSuggestion.None;
        }
        var targetIndex = -1;
        for (var index = 0; index < orderedStages.Count; index++)
        {
            if (string.Equals(orderedStages[index].Code, stageCode, StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = index;
                break;
            }
        }

        if (targetIndex <= 0)
        {
            return StageDateSuggestion.None;
        }

        var stageLookup = projectStages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (var index = targetIndex - 1; index >= 0; index--)
        {
            var predecessorDefinition = orderedStages[index];
            stageLookup.TryGetValue(predecessorDefinition.Code, out var predecessor);

            // A skipped stage does not provide a date boundary. Continue only
            // through a consecutive skipped chain; any other state is the
            // immediate effective predecessor and stops the search.
            if (predecessor?.Status == StageStatus.Skipped)
            {
                continue;
            }

            if (predecessor?.Status == StageStatus.Completed && predecessor.CompletedOn.HasValue)
            {
                return new StageDateSuggestion(
                    predecessor.CompletedOn.Value.AddDays(1),
                    predecessorDefinition.Code,
                    predecessorDefinition.Name,
                    predecessor.CompletedOn.Value,
                    SkippedStageCount: targetIndex - index - 1);
            }

            return new StageDateSuggestion(
                SuggestedStartDate: null,
                SourceStageCode: predecessorDefinition.Code,
                SourceStageName: predecessorDefinition.Name,
                SourceCompletionDate: predecessor?.CompletedOn,
                SkippedStageCount: targetIndex - index - 1);
        }

        return StageDateSuggestion.None;
    }
}

public sealed record StageDateSuggestion(
    DateOnly? SuggestedStartDate,
    string? SourceStageCode,
    string? SourceStageName,
    DateOnly? SourceCompletionDate,
    int SkippedStageCount)
{
    public static StageDateSuggestion None { get; } = new(null, null, null, null, 0);

    public bool HasSuggestion => SuggestedStartDate.HasValue;
}
