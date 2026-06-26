using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Shared helper that determines the present stage snapshot for a project and its age.
/// </summary>
public static class PresentStageHelper
{
    // SECTION: Canonical lifecycle resolution includes workflow stages that do not yet have database rows.
    public static ProjectStage? Resolve(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ResolveOrdered(BuildWorkflowStages(project));
    }

    public static IReadOnlyList<ProjectStage> BuildWorkflowStages(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var persisted = (project.ProjectStages ?? Array.Empty<ProjectStage>())
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.StageCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(stage => stage.SortOrder).First(),
                StringComparer.OrdinalIgnoreCase);

        var result = new List<ProjectStage>();
        var workflowCodes = ProcurementWorkflow.StageCodesFor(project.WorkflowVersion);
        foreach (var code in workflowCodes)
        {
            if (persisted.Remove(code, out var stage))
            {
                result.Add(stage);
                continue;
            }

            result.Add(new ProjectStage
            {
                ProjectId = project.Id,
                StageCode = code,
                SortOrder = ProcurementWorkflow.OrderOf(project.WorkflowVersion, code),
                Status = StageStatus.NotStarted
            });
        }

        result.AddRange(persisted.Values);
        return result
            .OrderBy(stage => ProcurementWorkflow.OrderOf(project.WorkflowVersion, stage.StageCode))
            .ThenBy(stage => stage.SortOrder)
            .ThenBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // SECTION: Entity-stage resolution retained for callers that already supply a complete ordered sequence.
    public static ProjectStage? Resolve(IEnumerable<ProjectStage>? stages)
    {
        if (stages is null)
        {
            return null;
        }

        var orderedStages = stages
            .OrderBy(stage => stage.SortOrder)
            .ThenBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ResolveOrdered(orderedStages);
    }

    private static ProjectStage? ResolveOrdered(IReadOnlyList<ProjectStage> orderedStages)
    {
        if (orderedStages.Count == 0)
        {
            return null;
        }

        return orderedStages.FirstOrDefault(stage => stage.Status == StageStatus.InProgress)
            ?? orderedStages.FirstOrDefault(stage => stage.Status != StageStatus.Completed && stage.Status != StageStatus.Skipped)
            ?? orderedStages[^1];
    }

    public static PresentStageSnapshot ComputePresentStageAndAge(
        IReadOnlyList<ProjectStageStatusSnapshot> stages,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        string? workflowVersion,
        DateOnly? referenceDate = null)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(workflowStageMetadataProvider);

        var orderedStages = BuildWorkflowSnapshots(stages, workflowStageMetadataProvider, workflowVersion);
        if (orderedStages.Count == 0)
        {
            return PresentStageSnapshot.Empty;
        }

        var current = orderedStages.FirstOrDefault(stage => stage.Status == StageStatus.InProgress);
        if (current == default)
        {
            current = orderedStages.FirstOrDefault(stage => stage.Status != StageStatus.Completed && stage.Status != StageStatus.Skipped);
        }

        if (current == default)
        {
            current = orderedStages[^1];
        }

        ProjectStageStatusSnapshot? lastCompleted = orderedStages
            .Where(stage => stage.Status == StageStatus.Completed && stage.CompletedOn.HasValue)
            .OrderByDescending(stage => stage.CompletedOn)
            .Select(stage => (ProjectStageStatusSnapshot?)stage)
            .FirstOrDefault();

        var today = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);

        int? days = null;
        if (current.Status == StageStatus.InProgress && current.ActualStart.HasValue)
        {
            days = Math.Max(0, today.DayNumber - current.ActualStart.Value.DayNumber);
        }
        else if (lastCompleted?.CompletedOn is { } completedOn)
        {
            days = Math.Max(0, today.DayNumber - completedOn.DayNumber);
        }

        return new PresentStageSnapshot(
            current.StageCode,
            workflowStageMetadataProvider.GetDisplayName(workflowVersion, current.StageCode),
            current.Status == StageStatus.InProgress,
            days,
            current.ActualStart,
            lastCompleted?.CompletedOn);
    }

    public static PresentStageSnapshot ComputePresentStageAndAge(
        int projectId,
        IReadOnlyDictionary<int, IReadOnlyList<ProjectStageStatusSnapshot>> stagesLookup,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IReadOnlyDictionary<int, string?> workflowVersions,
        DateOnly? referenceDate = null)
    {
        ArgumentNullException.ThrowIfNull(stagesLookup);
        ArgumentNullException.ThrowIfNull(workflowStageMetadataProvider);
        ArgumentNullException.ThrowIfNull(workflowVersions);

        var stages = stagesLookup.TryGetValue(projectId, out var foundStages)
            ? foundStages
            : Array.Empty<ProjectStageStatusSnapshot>();

        workflowVersions.TryGetValue(projectId, out var workflowVersion);

        return ComputePresentStageAndAge(
            stages,
            workflowStageMetadataProvider,
            workflowVersion,
            referenceDate);
    }

    private static IReadOnlyList<ProjectStageStatusSnapshot> BuildWorkflowSnapshots(
        IReadOnlyList<ProjectStageStatusSnapshot> stages,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        string? workflowVersion)
    {
        var persisted = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(stage => stage.SortOrder).First(),
                StringComparer.OrdinalIgnoreCase);

        var definitions = workflowStageMetadataProvider.GetStages(workflowVersion);
        var result = new List<ProjectStageStatusSnapshot>(Math.Max(stages.Count, definitions.Count));

        for (var index = 0; index < definitions.Count; index++)
        {
            var code = definitions[index].Code;
            if (persisted.Remove(code, out var stage))
            {
                result.Add(stage with { SortOrder = index });
            }
            else
            {
                result.Add(new ProjectStageStatusSnapshot(
                    code,
                    StageStatus.NotStarted,
                    index,
                    null,
                    null));
            }
        }

        var nextOrder = definitions.Count;
        foreach (var stage in persisted.Values
                     .OrderBy(stage => stage.SortOrder)
                     .ThenBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(stage with { SortOrder = nextOrder++ });
        }

        return result;
    }
}

public readonly record struct ProjectStageStatusSnapshot(
    string StageCode,
    StageStatus Status,
    int SortOrder,
    DateOnly? ActualStart,
    DateOnly? CompletedOn);

public sealed record PresentStageSnapshot(
    string? CurrentStageCode,
    string? CurrentStageName,
    bool IsCurrentStageInProgress,
    int? DaysSinceStartOrLastCompletion,
    DateOnly? CurrentStageStartDate,
    DateOnly? LastCompletedDate)
{
    public static PresentStageSnapshot Empty { get; } = new(null, null, false, null, null, null);
}
