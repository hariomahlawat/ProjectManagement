using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Resolves a project's operational lifecycle position from the canonical
/// workflow sequence. The resolver is intentionally database-independent so
/// every project surface can use the same ordering and current-stage rules.
/// </summary>
public static class ProjectLifecyclePositionResolver
{
    /// <summary>
    /// Builds the canonical procurement-stage sequence for a project.
    /// Transfer of Technology is excluded because it is managed separately
    /// after project completion.
    /// </summary>
    public static IReadOnlyList<ProjectLifecycleStageSnapshot> BuildProjectStages(
        Project project,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(workflowStageMetadataProvider);

        var persistedStages = project.ProjectStages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(HasRecordedActivity)
                    .ThenBy(stage => stage.SortOrder)
                    .ThenBy(stage => stage.Id)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        return workflowStageMetadataProvider
            .GetStages(project.WorkflowVersion)
            .Where(definition => !StageCodes.IsTot(definition.Code))
            .Select((definition, index) =>
            {
                persistedStages.TryGetValue(definition.Code, out var persisted);

                return new ProjectLifecycleStageSnapshot(
                    definition.Code,
                    definition.Name,
                    persisted?.Status ?? StageStatus.NotStarted,
                    index,
                    persisted?.CompletedOn,
                    persisted is not null && HasRecordedActivity(persisted));
            })
            .ToArray();
    }

    public static ProjectLifecyclePositionSnapshot Resolve(
        IEnumerable<ProjectLifecycleStageSnapshot>? stages)
    {
        var ordered = stages is null
            ? Array.Empty<ProjectLifecycleStageSnapshot>()
            : stages
                .OrderBy(stage => stage.SortOrder)
                .ThenBy(stage => stage.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (ordered.Length == 0)
        {
            return ProjectLifecyclePositionSnapshot.Empty;
        }

        var isConcluded = ordered.All(stage =>
            stage.Status is StageStatus.Completed or StageStatus.Skipped);

        var current = isConcluded
            ? null
            : ordered.FirstOrDefault(stage => stage.Status == StageStatus.InProgress)
                ?? ordered.FirstOrDefault(stage =>
                    stage.Status is not StageStatus.Completed and not StageStatus.Skipped);

        var next = current is null
            ? null
            : ordered.FirstOrDefault(stage =>
                stage.SortOrder > current.SortOrder &&
                stage.Status is StageStatus.NotStarted or StageStatus.Blocked);

        var lastCompleted = ordered
            .Where(stage => stage.Status == StageStatus.Completed)
            .OrderByDescending(stage => stage.SortOrder)
            .ThenByDescending(stage => stage.CompletedOn ?? DateOnly.MinValue)
            .FirstOrDefault();

        var previousCompleted = current is null
            ? lastCompleted
            : ordered
                .Where(stage =>
                    stage.SortOrder < current.SortOrder &&
                    stage.Status == StageStatus.Completed)
                .OrderByDescending(stage => stage.SortOrder)
                .ThenByDescending(stage => stage.CompletedOn ?? DateOnly.MinValue)
                .FirstOrDefault();

        return new ProjectLifecyclePositionSnapshot(
            current,
            next,
            previousCompleted,
            lastCompleted,
            isConcluded,
            ordered.Any(stage => stage.HasRecordedActivity));
    }

    private static bool HasRecordedActivity(ProjectStage stage) =>
        stage.Status != StageStatus.NotStarted ||
        stage.PlannedStart.HasValue ||
        stage.PlannedDue.HasValue ||
        stage.ActualStart.HasValue ||
        stage.CompletedOn.HasValue;
}

public sealed record ProjectLifecycleStageSnapshot(
    string Code,
    string Name,
    StageStatus Status,
    int SortOrder,
    DateOnly? CompletedOn,
    bool HasRecordedActivity);

public sealed record ProjectLifecyclePositionSnapshot(
    ProjectLifecycleStageSnapshot? CurrentStage,
    ProjectLifecycleStageSnapshot? NextStage,
    ProjectLifecycleStageSnapshot? PreviousCompletedStage,
    ProjectLifecycleStageSnapshot? LastCompletedStage,
    bool IsConcluded,
    bool HasRecordedHistory)
{
    public static ProjectLifecyclePositionSnapshot Empty { get; } = new(
        null,
        null,
        null,
        null,
        false,
        false);
}
